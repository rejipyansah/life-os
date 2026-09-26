using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOS.Api.Services;

namespace LifeOS.Tests;

public class InterpretResultDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };

    [Fact]
    public void Deserialize_Expense_GeminiOutput()
    {
        var json = """
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18000,
            "description": "Jajan",
            "account": "Cash",
            "toAccount": null,
            "date": "2026-09-18",
            "feeAmount": null,
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Equal("Expense", result.TransactionType);
        Assert.Equal(18000m, result.Amount);
        Assert.Equal("Jajan", result.Description);
        Assert.Equal("Cash", result.Account);
        Assert.Null(result.ToAccount);
        Assert.Equal("2026-09-18", result.Date);
        Assert.Null(result.FeeAmount);
        Assert.Empty(result.ClarificationFields);
    }

    [Fact]
    public void Deserialize_Transfer_GeminiOutput()
    {
        var json = """
        {
            "intent": "CreateTransaction",
            "transactionType": "Transfer",
            "amount": 500000,
            "account": "Mandiri",
            "toAccount": "SeaBank",
            "date": "2026-09-18",
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("Transfer", result.TransactionType);
        Assert.Equal(500000m, result.Amount);
        Assert.Equal("Mandiri", result.Account);
        Assert.Equal("SeaBank", result.ToAccount);
    }

    [Fact]
    public void Deserialize_Unsupported_GeminiOutput()
    {
        var json = """
        {
            "intent": "Unsupported",
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("Unsupported", result.Intent);
        Assert.Null(result.TransactionType);
        Assert.Null(result.Amount);
    }

    [Fact]
    public void Deserialize_WithClarificationFields_GeminiOutput()
    {
        var json = """
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18000,
            "account": null,
            "date": "2026-09-18",
            "clarificationFields": ["account"]
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Null(result.Account);
        Assert.Single(result.ClarificationFields);
        Assert.Contains("account", result.ClarificationFields);
    }

    [Fact]
    public void Deserialize_MissingFields_GeminiOutput()
    {
        // Gemini might omit null fields entirely
        var json = """
        {
            "intent": "CreateTransaction",
            "clarificationFields": ["account", "amount"]
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("CreateTransaction", result.Intent);
        Assert.Null(result.TransactionType);
        Assert.Null(result.Amount);
        Assert.Null(result.Account);
    }

    [Fact]
    public void Deserialize_AmountAsInteger_GeminiOutput()
    {
        // Gemini may return amounts as integers
        var json = """
        {
            "intent": "CreateTransaction",
            "transactionType": "Income",
            "amount": 5000000,
            "account": "Mandiri",
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(5000000m, result.Amount);
    }

    [Fact]
    public void Deserialize_AmountAsDecimal_GeminiOutput()
    {
        // Gemini may return amounts with decimals
        var json = """
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18500.50,
            "account": "Cash",
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(18500.50m, result.Amount);
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsJsonException()
    {
        var json = "not valid json {{{";
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<InterpretResult>(json, Options));
        Assert.Contains("invalid JSON", ex.Message);
    }

    [Fact]
    public void Deserialize_EmptyObject_WithDefaults()
    {
        var json = "{}";
        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);
        Assert.NotNull(result);
        Assert.Equal("", result.Intent);
        Assert.Null(result.TransactionType);
        Assert.Null(result.Amount);
        Assert.Empty(result.ClarificationFields);
    }

    [Fact]
    public void Deserialize_CreateAllocation_GeminiOutput()
    {
        var json = """
        {
            "intent": "CreateAllocation",
            "amount": 350000,
            "allocationName": "WiFi",
            "account": "Mandiri",
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("CreateAllocation", result.Intent);
        Assert.Equal(350000m, result.Amount);
        Assert.Equal("WiFi", result.AllocationName);
        Assert.Equal("Mandiri", result.Account);
        Assert.Null(result.TransactionType);
        Assert.Empty(result.ClarificationFields);
    }

    [Fact]
    public void Deserialize_CreateAllocation_MissingName_GeminiOutput()
    {
        var json = """
        {
            "intent": "CreateAllocation",
            "amount": 350000,
            "account": "Mandiri",
            "clarificationFields": []
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("CreateAllocation", result.Intent);
        Assert.Null(result.AllocationName);
    }

    [Fact]
    public void Deserialize_ExtraFields_Ignored()
    {
        var json = """
        {
            "intent": "CreateTransaction",
            "transactionType": "Expense",
            "amount": 18000,
            "account": "Cash",
            "clarificationFields": [],
            "scopeId": "should-be-ignored",
            "accountId": "should-be-ignored",
            "userId": "should-be-ignored"
        }
        """;

        var result = JsonSerializer.Deserialize<InterpretResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("CreateTransaction", result.Intent);
        // Verify no ScopeId/AccountId/UserId property exists on InterpretResult
        Assert.False(typeof(InterpretResult).GetProperty("ScopeId")?.GetValue(result) != null);
        Assert.False(typeof(InterpretResult).GetProperty("AccountId")?.GetValue(result) != null);
        Assert.False(typeof(InterpretResult).GetProperty("UserId")?.GetValue(result) != null);
    }
}
