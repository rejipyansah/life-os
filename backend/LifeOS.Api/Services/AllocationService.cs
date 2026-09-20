using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public class AllocationService
{
    private readonly ApplicationDbContext _db;

    public AllocationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Allocation> CreateAllocationAsync(CreateAllocationCommand command, CancellationToken ct = default)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Allocation Amount must be greater than 0.");

        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Allocation Name is required.");

        // Validate Account belongs to the current Scope
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.AccountId && a.ScopeId == command.ScopeId, ct);

        if (account is null)
            throw new ValidationException("Account not found in current Scope.");

        var allocation = new Allocation
        {
            ScopeId = command.ScopeId,
            AccountId = command.AccountId,
            Name = command.Name.Trim(),
            Amount = command.Amount,
            IsActive = true
        };

        _db.Allocations.Add(allocation);
        await _db.SaveChangesAsync(ct);

        return allocation;
    }

    public async Task<List<Allocation>> GetAllocationsAsync(Guid scopeId, CancellationToken ct = default)
    {
        return await _db.Allocations
            .Where(a => a.ScopeId == scopeId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Allocation> UpdateAllocationAsync(Guid allocationId, UpdateAllocationCommand command, CancellationToken ct = default)
    {
        var allocation = await _db.Allocations
            .FirstOrDefaultAsync(a => a.Id == allocationId && a.ScopeId == command.ScopeId, ct);

        if (allocation is null)
            throw new ValidationException("Allocation not found in current Scope.");

        if (command.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
                throw new ValidationException("Allocation Name cannot be empty.");
            allocation.Name = command.Name.Trim();
        }

        if (command.Amount.HasValue)
        {
            if (command.Amount.Value <= 0)
                throw new ValidationException("Allocation Amount must be greater than 0.");
            allocation.Amount = command.Amount.Value;
        }

        if (command.IsActive.HasValue)
        {
            allocation.IsActive = command.IsActive.Value;
        }

        if (command.AccountId.HasValue)
        {
            var newAccount = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Id == command.AccountId.Value && a.ScopeId == command.ScopeId, ct);

            if (newAccount is null)
                throw new ValidationException("Account not found in current Scope.");

            allocation.AccountId = command.AccountId.Value;
        }

        await _db.SaveChangesAsync(ct);

        return allocation;
    }

    public async Task<decimal> GetAllocatedAmountAsync(Guid accountId, Guid scopeId, CancellationToken ct = default)
    {
        return await _db.Allocations
            .Where(a => a.AccountId == accountId && a.ScopeId == scopeId && a.IsActive)
            .SumAsync(a => a.Amount, ct);
    }
}
