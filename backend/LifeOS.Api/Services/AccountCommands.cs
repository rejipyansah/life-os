using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public class CreateAccountCommand
{
    public Guid ScopeId { get; set; }
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
}

public class UpdateAccountCommand
{
    public Guid ScopeId { get; set; }
    public string? Name { get; set; }
    public AccountType? Type { get; set; }
    public bool? IsArchived { get; set; }
}

public class AccountProjection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>Saldo riil = SUM(TransactionEntry.Amount). Lokasi uang di akun ini.</summary>
    public decimal ActualBalance { get; set; }

    /// <summary>
    /// SELALU 0 — alokasi (Dana yang Disisihkan) scope-wide, bukan milik akun tertentu.
    /// </summary>
    public decimal SetAsideAmount { get; set; }

    /// <summary>
    /// Saldo aktual akun ini. Alokasi dihitung di TotalAvailable scope-wide.
    /// </summary>
    public decimal AvailableBalance { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class AccountListProjection
{
    public List<AccountProjection> Accounts { get; set; } = [];

    /// <summary>Σ saldo aktual seluruh akun dalam scope.</summary>
    public decimal TotalActualBalance { get; set; }

    /// <summary>Σ alokasi aktif scope-wide — bukan per akun.</summary>
    public decimal TotalSetAsideAmount { get; set; }

    /// <summary>TotalAvailable = TotalActualBalance − TotalSetAsideAmount.</summary>
    public decimal TotalAvailableBalance { get; set; }
}
