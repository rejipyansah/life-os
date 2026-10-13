using System.Net;
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

    public GroqInterpreter(IConfiguration configuration, HttpClient httpClient)
    {
        var apiKey = configuration["Groq:ApiKey"]
            ?? throw new InvalidOperationException("Groq:ApiKey is not configured.");

        _model = configuration["Groq:Model"] ?? "openai/gpt-oss-20b";
        _reasoningEffort = configuration["Groq:ReasoningEffort"] ?? "low";
        var endpoint = configuration["Groq:Endpoint"] ?? "https://api.groq.com/openai/v1/";
        if (!endpoint.EndsWith("/", StringComparison.Ordinal))
            endpoint += "/";

        _http = httpClient;
        _http.BaseAddress = new Uri(endpoint, UriKind.Absolute);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<InterpretResult> InterpretAsync(InterpretRequest request, CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(request.CurrentDate, request.EligibleAccounts);
        var requestBody = BuildRequestBody(_model, _reasoningEffort, systemPrompt, request.Input);

        try
        {
            var httpResponse = await _http.PostAsJsonAsync("chat/completions", requestBody, ct);
            var body = await httpResponse.Content.ReadAsStringAsync(ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                if (IsQuotaExceeded(httpResponse.StatusCode, body))
                    throw new InterpreterQuotaExceededException("Groq usage quota has been reached.");
                throw new InterpreterProviderException(
                    $"Groq API returned {(int)httpResponse.StatusCode}: {body}");
            }

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

    private static object BuildRequestBody(
        string model,
        string reasoningEffort,
        string systemPrompt,
        string userMessage)
    {
        return new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.1,
            reasoning_effort = reasoningEffort,
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

    private static bool IsQuotaExceeded(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
            return true;

        if ((int)statusCode is not (402 or 403))
            return false;

        return body.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || body.Contains("billing", StringComparison.OrdinalIgnoreCase)
            || body.Contains("limit", StringComparison.OrdinalIgnoreCase)
            || body.Contains("credit", StringComparison.OrdinalIgnoreCase);
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
                ["categoryName"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("Makan & Minum", "Transportasi", "Tagihan & Utilitas", "Belanja", "Kesehatan", "Hiburan", "Pendidikan", "Pendapatan", "Lainnya")
                        },
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
                        ["enum"] = new JsonArray("amount", "account", "toAccount", "transactionType", "setAsideName", "title", "direction", "date")
                    }
                }
            },
            ["required"] = new JsonArray(
                "intent", "transactionType", "amount", "description", "categoryName",
                "account", "toAccount", "date", "feeAmount",
                "setAsideName", "title", "direction", "clarificationFields"
            ),
            ["additionalProperties"] = false
        };
    }
}
