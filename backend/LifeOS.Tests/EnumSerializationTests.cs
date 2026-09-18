using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOS.Api.Models;
using LifeOS.Api.Services;

namespace LifeOS.Tests;

public class EnumSerializationTests
{
    private readonly JsonSerializerOptions _options;

    public EnumSerializationTests()
    {
        _options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
        };
    }

    // ───────────────────────── AccountType ─────────────────────────

    [Fact]
    public void AccountType_Cash_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(AccountType.Cash, _options);
        Assert.Equal("\"Cash\"", json);
    }

    [Fact]
    public void AccountType_Bank_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(AccountType.Bank, _options);
        Assert.Equal("\"Bank\"", json);
    }

    [Fact]
    public void AccountType_EWallet_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(AccountType.EWallet, _options);
        Assert.Equal("\"EWallet\"", json);
    }

    [Theory]
    [InlineData("\"Cash\"", AccountType.Cash)]
    [InlineData("\"Bank\"", AccountType.Bank)]
    [InlineData("\"EWallet\"", AccountType.EWallet)]
    public void AccountType_DeserializesFromString(string json, AccountType expected)
    {
        var result = JsonSerializer.Deserialize<AccountType>(json, _options);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AccountType_InvalidString_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AccountType>("\"Invalid\"", _options));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void AccountType_NumericValue_Rejected(string numericValue)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AccountType>(numericValue, _options));
    }

    // ───────────────────────── TransactionType ─────────────────────────

    [Fact]
    public void TransactionType_Income_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(TransactionType.Income, _options);
        Assert.Equal("\"Income\"", json);
    }

    [Fact]
    public void TransactionType_Expense_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(TransactionType.Expense, _options);
        Assert.Equal("\"Expense\"", json);
    }

    [Fact]
    public void TransactionType_Transfer_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(TransactionType.Transfer, _options);
        Assert.Equal("\"Transfer\"", json);
    }

    [Fact]
    public void TransactionType_Refund_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(TransactionType.Refund, _options);
        Assert.Equal("\"Refund\"", json);
    }

    [Fact]
    public void TransactionType_Reversal_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(TransactionType.Reversal, _options);
        Assert.Equal("\"Reversal\"", json);
    }

    [Fact]
    public void TransactionType_Adjustment_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(TransactionType.Adjustment, _options);
        Assert.Equal("\"Adjustment\"", json);
    }

    [Theory]
    [InlineData("\"Income\"", TransactionType.Income)]
    [InlineData("\"Expense\"", TransactionType.Expense)]
    [InlineData("\"Transfer\"", TransactionType.Transfer)]
    [InlineData("\"Refund\"", TransactionType.Refund)]
    [InlineData("\"Reversal\"", TransactionType.Reversal)]
    [InlineData("\"Adjustment\"", TransactionType.Adjustment)]
    public void TransactionType_DeserializesFromString(string json, TransactionType expected)
    {
        var result = JsonSerializer.Deserialize<TransactionType>(json, _options);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TransactionType_InvalidString_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TransactionType>("\"Invalid\"", _options));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    public void TransactionType_NumericValue_Rejected(string numericValue)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TransactionType>(numericValue, _options));
    }

    // ───────────────────────── Round-trip ─────────────────────────

    [Fact]
    public void AccountType_Roundtrip_PreservesValue()
    {
        var original = AccountType.Bank;
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<AccountType>(json, _options);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void TransactionType_Roundtrip_PreservesValue()
    {
        var original = TransactionType.Transfer;
        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<TransactionType>(json, _options);
        Assert.Equal(original, deserialized);
    }

    // ───────────────────────── DTO round-trip ─────────────────────────

    [Fact]
    public void CreateAccountCommand_StringEnum_Deserialized()
    {
        var json = """{"ScopeId":"00000000-0000-0000-0000-000000000001","Name":"Test","Type":"Bank"}""";
        var command = JsonSerializer.Deserialize<CreateAccountCommand>(json, _options);

        Assert.NotNull(command);
        Assert.Equal(AccountType.Bank, command.Type);
    }

    [Fact]
    public void CreateAccountCommand_NumericEnum_Rejected()
    {
        var json = """{"ScopeId":"00000000-0000-0000-0000-000000000001","Name":"Test","Type":1}""";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateAccountCommand>(json, _options));
    }

    [Fact]
    public void CreateTransactionCommand_StringEnum_Deserialized()
    {
        var json = """
        {"ScopeId":"00000000-0000-0000-0000-000000000001","Type":"Expense","Amount":18000,
         "OccurredOn":"2026-09-18","Entries":[{"AccountId":"00000000-0000-0000-0000-000000000002","Amount":-18000}]}
        """;
        var command = JsonSerializer.Deserialize<CreateTransactionCommand>(json, _options);

        Assert.NotNull(command);
        Assert.Equal(TransactionType.Expense, command.Type);
    }

    [Fact]
    public void CreateTransactionCommand_NumericEnum_Rejected()
    {
        var json = """
        {"ScopeId":"00000000-0000-0000-0000-000000000001","Type":1,"Amount":18000,
         "OccurredOn":"2026-09-18","Entries":[{"AccountId":"00000000-0000-0000-0000-000000000002","Amount":-18000}]}
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateTransactionCommand>(json, _options));
    }

    [Fact]
    public void CreateAccountCommand_InvalidEnumString_ThrowsJsonException()
    {
        var json = """{"ScopeId":"00000000-0000-0000-0000-000000000001","Name":"Test","Type":"Invalid"}""";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateAccountCommand>(json, _options));
    }

    [Fact]
    public void CreateTransactionCommand_InvalidEnumString_ThrowsJsonException()
    {
        var json = """
        {"ScopeId":"00000000-0000-0000-0000-000000000001","Type":"Invalid","Amount":18000,
         "OccurredOn":"2026-09-18","Entries":[{"AccountId":"00000000-0000-0000-0000-000000000002","Amount":-18000}]}
        """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CreateTransactionCommand>(json, _options));
    }

    // ───────────────────────── ScopeType (not exposed, but verify) ─────────────────────────

    [Fact]
    public void ScopeType_Owner_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(ScopeType.Owner, _options);
        Assert.Equal("\"Owner\"", json);
    }

    [Fact]
    public void ScopeType_Guest_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(ScopeType.Guest, _options);
        Assert.Equal("\"Guest\"", json);
    }
}
