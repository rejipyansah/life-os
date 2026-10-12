using LifeOS.Api.Services;

namespace LifeOS.Tests;

public sealed class FinanceValidationErrorCatalogTests
{
    [Theory]
    [InlineData("Insufficient balance in 'Tunai'. Balance: 100, needed: 250.", "insufficient_balance", "Saldo sumber dana tidak mencukupi.")]
    [InlineData("Invalid AccountType: 999", "account_type_invalid", "Jenis sumber dana tidak valid.")]
    [InlineData("Akun dengan nama 'Tunai' sudah ada dalam scope ini.", "duplicate_account_name", "Nama sumber dana tersebut sudah digunakan pada ruang data ini.")]
    [InlineData("Transaction Amount must not exceed 9,000.", "transaction_amount_too_large", "Nominal transaksi melebihi batas yang diizinkan.")]
    [InlineData("Agenda amount cannot exceed 1,000.", "agenda_amount_too_large", "Nominal agenda melebihi batas yang diizinkan.")]
    public void Resolve_returns_stable_code_and_localized_message(
        string sourceMessage,
        string expectedCode,
        string expectedMessage)
    {
        var error = FinanceValidationErrorCatalog.Resolve(sourceMessage);

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain(sourceMessage, error.Message);
    }

    [Fact]
    public void Resolve_uses_generic_localized_fallback_for_unknown_validation()
    {
        var error = FinanceValidationErrorCatalog.Resolve("New untranslated validation detail");

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("Data Finance tidak valid. Periksa kembali isian.", error.Message);
    }
}
