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

    /// <summary>Saldo riil = SUM(TransactionEntry.Amount).</summary>
    public decimal ActualBalance { get; set; }

    /// <summary>Uang yang sedang disisihkan pada akun ini.</summary>
    public decimal SetAsideAmount { get; set; }

    /// <summary>Saldo tersedia = ActualBalance - SetAsideAmount.</summary>
    public decimal AvailableBalance { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class AccountListProjection
{
    public List<AccountProjection> Accounts { get; set; } = [];
    public decimal TotalActualBalance { get; set; }
    public decimal TotalSetAsideAmount { get; set; }
    public decimal TotalAvailableBalance { get; set; }
}
