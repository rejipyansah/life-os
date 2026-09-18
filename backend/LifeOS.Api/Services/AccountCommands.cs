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
    public decimal Balance { get; set; }
    public decimal Allocated { get; set; }
    public decimal Available { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AccountListProjection
{
    public List<AccountProjection> Accounts { get; set; } = [];
    public decimal TotalBalance { get; set; }
    public decimal TotalAllocated { get; set; }
    public decimal TotalAvailable { get; set; }
}
