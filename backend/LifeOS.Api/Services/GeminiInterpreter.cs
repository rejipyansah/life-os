using System.Text.Json;
using System.Text.Json.Serialization;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;

namespace LifeOS.Api.Services;

public class GeminiInterpreter : IInterpreter
{
    private readonly Client _client;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };

    public GeminiInterpreter(IConfiguration configuration)
    {
        var apiKey = configuration["Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        _model = configuration["Gemini:Model"] ?? "gemini-3.6-flash";
        _client = new Client(apiKey: apiKey);
    }

    public async Task<InterpretResult> InterpretAsync(InterpretRequest request, CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(request.CurrentDate, request.EligibleAccounts);
        var schema = BuildSchema();

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content
            {
                Parts = [new Part { Text = systemPrompt }]
            },
            ResponseMimeType = "application/json",
            ResponseSchema = schema,
            Temperature = 0.1
        };

        try
        {
            var response = await _client.Models.GenerateContentAsync(
                model: _model,
                contents: request.Input,
                config: config);

            var text = response.Candidates?[0]?.Content?.Parts?[0]?.Text;
            if (string.IsNullOrEmpty(text))
                return new InterpretResult { Intent = "Unsupported", Clarifications = ["Empty response from AI provider."] };

            return JsonSerializer.Deserialize<InterpretResult>(text, JsonOptions)
                ?? new InterpretResult { Intent = "Unsupported", Clarifications = ["Failed to parse AI response."] };
        }
        catch (InterpreterProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InterpreterProviderException("Failed to communicate with AI provider.", ex);
        }
    }

    private static string BuildSystemPrompt(DateOnly currentDate, IReadOnlyList<string> accounts)
    {
        var accountList = string.Join(", ", accounts);
        return $$"""
            You are a finance interpreter for Life OS. Parse the user's natural language input and return a structured JSON interpretation.

            Rules:
            - The user may use Indonesian or English casual language.
            - Amount abbreviations: "rb" or "k" = thousand (×1,000), "jt" = million (×1,000,000), "M" = billion (×1,000,000,000).
            - "18rb" = 18000, "50k" = 50000, "1jt" = 1000000, "1.5jt" = 1500000.
            - Today's date is {{currentDate:yyyy-MM-dd}}.
            - Available accounts: {{accountList}}.
            - For Expense: spending money from an account. Amount is positive. Account is the source.
            - For Income: receiving money into an account. Amount is positive. Account is the destination.
            - For Transfer: moving money between accounts. Amount is positive. Account is source, toAccount is destination.
            - Never fabricate account names. Only use names from the provided list.
            - Return intent="Unsupported" if the input is not a financial transaction.
            - If you need more information (e.g., which account, how much), add a clarification message and leave the missing field null.
            - Use today's date as the default date if not specified.
            - Date format: YYYY-MM-DD.
            """;
    }

    private static Schema BuildSchema()
    {
        return new Schema
        {
            Type = Google.GenAI.Types.Type.Object,
            Properties = new Dictionary<string, Schema>
            {
                { "intent", new Schema { Type = Google.GenAI.Types.Type.String, Enum = ["CreateTransaction", "Unsupported"] } },
                { "transactionType", new Schema { Type = Google.GenAI.Types.Type.String, Enum = ["Expense", "Income", "Transfer"] } },
                { "amount", new Schema { Type = Google.GenAI.Types.Type.Number } },
                { "description", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "account", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "toAccount", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "date", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "feeAmount", new Schema { Type = Google.GenAI.Types.Type.Number } },
                { "clarifications", new Schema { Type = Google.GenAI.Types.Type.Array, Items = new Schema { Type = Google.GenAI.Types.Type.String } } }
            },
            Required = ["intent", "clarifications"],
            Title = "FinanceInterpretation",
            PropertyOrdering = ["intent", "transactionType", "amount", "description", "account", "toAccount", "date", "feeAmount", "clarifications"]
        };
    }
}
