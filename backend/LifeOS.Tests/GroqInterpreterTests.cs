using System.Net;
using System.Text.Json;
using LifeOS.Api.Services;
using Microsoft.Extensions.Configuration;

namespace LifeOS.Tests;

public class GroqInterpreterTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly GroqInterpreter _interpreter;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GroqInterpreterTests()
    {
        _handler = new MockHttpMessageHandler();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-20b",
                ["Groq:ReasoningEffort"] = "low"
            })
            .Build();

        _interpreter = new GroqInterpreter(config, _handler);
    }

    public void Dispose() { }

    private InterpretRequest MakeRequest(string input) => new()
    {
        Input = input,
        CurrentDate = new DateOnly(2026, 9, 26),
        EligibleAccounts = ["Cash", "Mandiri", "BCA"]
    };

    private void SetResponse(string content) =>
        _handler.ResponseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content } }
            }
        });

    private void SetErrorResponse(HttpStatusCode statusCode, string errorBody = "") =>
        (_handler.StatusCode, _handler.ResponseContent) = (statusCode, errorBody);

    // ───────────────────── 1. Valid Expense ─────────────────────

    [Fact]
    public async Task Interpret_Expense_ReturnsCorrectResult()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18000,
            "description": "Jajan",
            "account": "Cash",
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("jajan 18rb cash"));

        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Equal("Expense", result.TransactionType);
        Assert.Equal(18000m, result.Amount);
        Assert.Equal("Cash", result.Account);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── 2. Valid Income ─────────────────────

    [Fact]
    public async Task Interpret_Income_ReturnsCorrectResult()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Income",
            "amount": 500000,
            "description": "Gaji",
            "account": "Mandiri",
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("gaji 500k mandiri"));

        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Equal("Income", result.TransactionType);
        Assert.Equal(500000m, result.Amount);
        Assert.Equal("Mandiri", result.Account);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── 3. Valid Transfer ─────────────────────

    [Fact]
    public async Task Interpret_Transfer_ReturnsCorrectResult()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Transfer",
            "amount": 100000,
            "description": null,
            "account": "Mandiri",
            "toAccount": "BCA",
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("transfer 100rb dari Mandiri ke BCA"));

        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Equal("Transfer", result.TransactionType);
        Assert.Equal(100000m, result.Amount);
        Assert.Equal("Mandiri", result.Account);
        Assert.Equal("BCA", result.ToAccount);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── 4. Missing fields / clarificationFields ─────────────────────

    [Fact]
    public async Task Interpret_MissingAmount_ReturnsClarificationFields()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": null,
            "description": null,
            "account": "Cash",
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": ["amount"]
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("jajan"));

        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Null(result.Amount);
        Assert.Contains("amount", result.ClarificationFields);
    }

    [Fact]
    public async Task Interpret_MultipleMissingFields_ReturnsAllClarificationFields()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": null,
            "description": null,
            "account": null,
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": ["amount", "account"]
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("bayar listrik"));

        Assert.Contains("amount", result.ClarificationFields);
        Assert.Contains("account", result.ClarificationFields);
        Assert.Equal(2, result.ClarificationFields.Count);
    }

    // ───────────────────── 5. CreateAllocation ─────────────────────

    [Fact]
    public async Task Interpret_CreateAllocation_ReturnsCorrectResult()
    {
        SetResponse("""
        {
            "intent": "CreateAllocation",
            "transactionType": null,
            "amount": 350000,
            "description": null,
            "account": "Mandiri",
            "toAccount": null,
            "date": null,
            "feeAmount": null,
            "allocationName": "WiFi",
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("sisihkan 350rb buat wifi dari mandiri"));

        Assert.Equal("CreateAllocation", result.Intent);
        Assert.Null(result.TransactionType);
        Assert.Equal(350000m, result.Amount);
        Assert.Equal("Mandiri", result.Account);
        Assert.Equal("WiFi", result.AllocationName);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── 6. Unsupported ─────────────────────

    [Fact]
    public async Task Interpret_Unsupported_ReturnsUnsupported()
    {
        SetResponse("""
        {
            "intent": "Unsupported",
            "transactionType": null,
            "amount": null,
            "description": null,
            "account": null,
            "toAccount": null,
            "date": null,
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("cuaca hari ini"));

        Assert.Equal("Unsupported", result.Intent);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── 7. Decimal comma ─────────────────────

    [Fact]
    public async Task Interpret_DecimalComma_DeserializedCorrectly()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 1500000,
            "description": null,
            "account": "Cash",
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("bayar 1.5jt cash"));

        Assert.Equal(1500000m, result.Amount);
    }

    // ───────────────────── 8. Transfer source missing ─────────────────────

    [Fact]
    public async Task Interpret_TransferSourceMissing_ReturnsClarificationFields()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Transfer",
            "amount": 100000,
            "description": null,
            "account": null,
            "toAccount": "BCA",
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": ["account"]
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("transfer 100rb ke BCA"));

        Assert.Equal("Transfer", result.TransactionType);
        Assert.Null(result.Account);
        Assert.Contains("account", result.ClarificationFields);
    }

    // ───────────────────── 9. Invalid/unknown clarification field ─────────────────────

    [Fact]
    public async Task Interpret_InvalidClarificationField_DeserializedAsIs()
    {
        // Even if Groq somehow returns an invalid field, it should deserialize
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18000,
            "description": null,
            "account": "Cash",
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": ["unknown_field"]
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("jajan 18rb cash"));

        // The field is deserialized even though it's invalid;
        // InterpretEndpoint.ValidFields will filter it out deterministically
        Assert.Contains("unknown_field", result.ClarificationFields);
    }

    // ───────────────────── 10. Empty clarificationFields ─────────────────────

    [Fact]
    public async Task Interpret_EmptyClarificationFields_ReturnsEmptyList()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18000,
            "description": null,
            "account": "Cash",
            "toAccount": null,
            "date": "2026-09-26",
            "feeAmount": null,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("jajan 18rb cash"));

        Assert.NotNull(result.ClarificationFields);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── 11. HTTP error → InterpreterProviderException ─────────────────────

    [Fact]
    public async Task Interpret_HttpError_ThrowsInterpreterProviderException()
    {
        SetErrorResponse(HttpStatusCode.TooManyRequests, "rate limited");

        var ex = await Assert.ThrowsAsync<InterpreterProviderException>(
            () => _interpreter.InterpretAsync(MakeRequest("jajan 18rb cash")));
        Assert.Contains("429", ex.Message);
    }

    [Fact]
    public async Task Interpret_ServerError_ThrowsInterpreterProviderException()
    {
        SetErrorResponse(HttpStatusCode.InternalServerError, "internal error");

        var ex = await Assert.ThrowsAsync<InterpreterProviderException>(
            () => _interpreter.InterpretAsync(MakeRequest("jajan 18rb cash")));
        Assert.Contains("500", ex.Message);
    }

    // ───────────────────── 12. Empty/null content → Unsupported fallback ─────────────────────

    [Fact]
    public async Task Interpret_EmptyContent_ReturnsUnsupportedFallback()
    {
        _handler.ResponseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = (string?)null } }
            }
        });

        var result = await _interpreter.InterpretAsync(MakeRequest("test"));

        Assert.Equal("Unsupported", result.Intent);
    }

    [Fact]
    public async Task Interpret_EmptyStringContent_ReturnsUnsupportedFallback()
    {
        _handler.ResponseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "" } }
            }
        });

        var result = await _interpreter.InterpretAsync(MakeRequest("test"));

        Assert.Equal("Unsupported", result.Intent);
    }

    // ───────────────────── 13. Full round-trip JSON deserialization ─────────────────────

    [Fact]
    public async Task Interpret_FullResponse_AllFieldsDeserialized()
    {
        SetResponse("""
        {
            "intent": "CreateTransaction",
            "transactionType": "Transfer",
            "amount": 100000,
            "description": "Transfer savings",
            "account": "Mandiri",
            "toAccount": "BCA",
            "date": "2026-09-26",
            "feeAmount": 2500,
            "allocationName": null,
            "clarificationFields": []
        }
        """);

        var result = await _interpreter.InterpretAsync(MakeRequest("transfer 100rb Mandiri ke BCA fee 2500"));

        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Equal("Transfer", result.TransactionType);
        Assert.Equal(100000m, result.Amount);
        Assert.Equal("Transfer savings", result.Description);
        Assert.Equal("Mandiri", result.Account);
        Assert.Equal("BCA", result.ToAccount);
        Assert.Equal("2026-09-26", result.Date);
        Assert.Equal(2500m, result.FeeAmount);
        Assert.Null(result.AllocationName);
        Assert.Empty(result.ClarificationFields);
    }

    // ───────────────────── Helpers ─────────────────────

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseContent { get; set; } = "{}";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseContent, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
