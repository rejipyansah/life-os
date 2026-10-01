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
        _model = configuration["Gemini:Model"] ?? "gemini-3.5-flash-lite";
        _client = new Client(apiKey: apiKey);
    }

    public async Task<InterpretResult> InterpretAsync(InterpretRequest request, CancellationToken ct = default)
    {
        var systemPrompt = InterpreterPrompts.Build(request.CurrentDate, request.EligibleAccounts);
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
                return new InterpretResult { Intent = "Unsupported", ClarificationFields = ["amount"] };

            return JsonSerializer.Deserialize<InterpretResult>(text, JsonOptions)
                ?? new InterpretResult { Intent = "Unsupported", ClarificationFields = ["amount"] };
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

    private static Schema BuildSchema()
    {
        return new Schema
        {
            Type = Google.GenAI.Types.Type.Object,
            Properties = new Dictionary<string, Schema>
            {
                { "intent", new Schema { Type = Google.GenAI.Types.Type.String, Enum = ["CreateTransaction", "CreateSetAside", "CreateUpcomingEvent", "Unsupported"] } },
                { "transactionType", new Schema { Type = Google.GenAI.Types.Type.String, Enum = ["Expense", "Income", "Transfer"] } },
                { "amount", new Schema { Type = Google.GenAI.Types.Type.Number } },
                { "description", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "account", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "toAccount", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "date", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "feeAmount", new Schema { Type = Google.GenAI.Types.Type.Number } },
                { "setAsideName", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "title", new Schema { Type = Google.GenAI.Types.Type.String } },
                { "direction", new Schema { Type = Google.GenAI.Types.Type.String, Enum = ["Income", "Expense"] } },
                { "clarificationFields", new Schema { Type = Google.GenAI.Types.Type.Array, Items = new Schema { Type = Google.GenAI.Types.Type.String, Enum = ["amount", "account", "toAccount", "setAsideName", "title", "direction", "date"] } } }
            },
            Required = ["intent", "clarificationFields"],
            Title = "FinanceInterpretation",
            PropertyOrdering = ["intent", "transactionType", "amount", "description", "account", "toAccount", "date", "feeAmount", "setAsideName", "title", "direction", "clarificationFields"]
        };
    }
}
