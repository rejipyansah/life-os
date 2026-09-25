namespace LifeOS.Api.Services;

public class CreateAllocationCommand
{
    public Guid ScopeId { get; set; }
    public Guid AccountId { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public class UpdateAllocationCommand
{
    public Guid ScopeId { get; set; }
    public Guid? AccountId { get; set; }
    public string? Name { get; set; }
    public decimal? Amount { get; set; }
    public string? Status { get; set; }
}
