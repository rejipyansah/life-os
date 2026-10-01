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
        => InterpreterPrompts.Build(currentDate, accounts);

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
                    ["enum"] = new JsonArray("CreateTransaction", "CreateSetAside", "CreateUpcomingEvent", "Unsupported")
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
                ["setAsideName"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["title"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string" },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["direction"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Income", "Expense") },
                        new JsonObject { ["type"] = "null" }
                    )
                },
                ["clarificationFields"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray("amount", "account", "toAccount", "setAsideName", "title", "direction", "date")
                    }
                }
            },
            ["required"] = new JsonArray(
                "intent", "transactionType", "amount", "description",
                "account", "toAccount", "date", "feeAmount",
                "setAsideName", "title", "direction", "clarificationFields"
            ),
            ["additionalProperties"] = false
        };
    }
}
