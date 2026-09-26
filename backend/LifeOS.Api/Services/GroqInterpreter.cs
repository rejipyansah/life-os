using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace LifeOS.Api.Services;

public class GroqInterpreter : IInterpreter
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _reasoningEffort;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };

    public GroqInterpreter(IConfiguration configuration, HttpMessageHandler? handler = null)
    {
        var apiKey = configuration["Groq:ApiKey"]
            ?? throw new InvalidOperationException("Groq:ApiKey is not configured.");

        _model = configuration["Groq:Model"] ?? "openai/gpt-oss-20b";
        _reasoningEffort = configuration["Groq:ReasoningEffort"] ?? "low";

        _http = new HttpClient(handler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri("https://api.groq.com/openai/v1/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<InterpretResult> InterpretAsync(InterpretRequest request, CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(request.CurrentDate, request.EligibleAccounts);
        var requestBody = BuildRequestBody(systemPrompt, request.Input);

        try
        {
            var httpResponse = await _http.PostAsJsonAsync("chat/completions", requestBody, ct);
            var body = await httpResponse.Content.ReadAsStringAsync(ct);

            if (!httpResponse.IsSuccessStatusCode)
                throw new InterpreterProviderException(
                    $"Groq API returned {(int)httpResponse.StatusCode}: {body}");

            var doc = JsonNode.Parse(body);
            var content = doc?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

            if (string.IsNullOrEmpty(content))
                return new InterpretResult { Intent = "Unsupported", ClarificationFields = ["amount"] };

            return JsonSerializer.Deserialize<InterpretResult>(content, JsonOptions)
                ?? new InterpretResult { Intent = "Unsupported", ClarificationFields = ["amount"] };
        }
        catch (InterpreterProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InterpreterProviderException("Failed to communicate with Groq provider.", ex);
        }
    }

    private static object BuildRequestBody(string systemPrompt, string userMessage)
    {
        return new
        {
            model = "openai/gpt-oss-20b",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.1,
            reasoning_effort = "low",
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "FinanceInterpretation",
                    strict = true,
                    schema = BuildSchema()
                }
            }
        };
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
            - For CreateAllocation: setting aside money for a purpose (a reservation/intention, NOT a transaction). Amount is positive. Account is where the funds are reserved. allocationName is the purpose/name (e.g., "WiFi", "Vacation", "Emergency Fund").
            - Never fabricate account names. Only use names from the provided list.
            - Return intent="Unsupported" if the input is not a financial action.
            - If a required field is missing or ambiguous, add the field name to clarificationFields (e.g. "amount", "account", "toAccount", "allocationName"). Do NOT write free-text clarification messages.
            - Use today's date as the default date if not specified.
            - Date format: YYYY-MM-DD.
            """;
    }

    private static JsonObject BuildSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["intent"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("CreateTransaction", "CreateAllocation", "Unsupported")
                },
                ["transactionType"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Expense", "Income", "Transfer") },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["amount"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "number" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["description"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["account"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["toAccount"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["date"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["feeAmount"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "number" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["allocationName"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["clarificationFields"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("amount", "account", "toAccount", "allocationName")
                    }
                }
            },
            ["required"] = new JsonArray(
                "intent", "transactionType", "amount", "description",
                "account", "toAccount", "date", "feeAmount",
                "allocationName", "clarificationFields"
            ),
            ["additionalProperties"] = false
        };
    }
}
