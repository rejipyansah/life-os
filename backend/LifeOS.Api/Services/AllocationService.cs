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

    public async Task<Allocation> CompleteAllocationAsync(Guid allocationId, Guid scopeId, CancellationToken ct = default)
    {
        var allocation = await _db.Allocations
            .FirstOrDefaultAsync(a => a.Id == allocationId && a.ScopeId == scopeId, ct);

        if (allocation is null)
            throw new ValidationException("Allocation not found in current Scope.");

        if (allocation.Status == AllocationStatus.Completed)
            throw new ValidationException("Alokasi ini sudah selesai.");

        if (allocation.Status == AllocationStatus.Cancelled)
            throw new ValidationException("Alokasi ini sudah dibatalkan.");

        if (allocation.Status != AllocationStatus.Active)
            throw new ValidationException("Hanya alokasi aktif yang dapat diselesaikan.");

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == allocation.AccountId && a.ScopeId == scopeId, ct);

        if (account is null)
            throw new ValidationException("Account not found in current Scope.");

        if (account.IsArchived)
            throw new ValidationException("Cannot complete allocation on an archived Account.");

        var balance = await _db.TransactionEntries
            .Where(te => te.AccountId == allocation.AccountId)
            .SumAsync(te => te.Amount, ct);

        if (balance < allocation.Amount)
            throw new ValidationException("Insufficient balance to complete this allocation.");

        var scope = await _db.Scopes.FirstAsync(s => s.Id == scopeId, ct);

        await using var dbTransaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            allocation.Status = AllocationStatus.Completed;

            var expense = new Transaction
            {
                ScopeId = scopeId,
                Scope = scope,
                Type = TransactionType.Expense,
                Amount = allocation.Amount,
                Description = $"Alokasi diselesaikan: {allocation.Name}",
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
                CreatedAt = DateTime.UtcNow
            };

            var entry = new TransactionEntry
            {
                AccountId = allocation.AccountId,
                Account = account,
                Transaction = expense,
                Amount = -allocation.Amount
            };

            _db.Transactions.Add(expense);
            _db.TransactionEntries.Add(entry);

            await _db.SaveChangesAsync(ct);
            await dbTransaction.CommitAsync(ct);

            return allocation;
        }
        catch
        {
            await dbTransaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<Allocation> CancelAllocationAsync(Guid allocationId, Guid scopeId, CancellationToken ct = default)
    {
        var allocation = await _db.Allocations
            .FirstOrDefaultAsync(a => a.Id == allocationId && a.ScopeId == scopeId, ct);

        if (allocation is null)
            throw new ValidationException("Allocation not found in current Scope.");

        if (allocation.Status == AllocationStatus.Completed)
            throw new ValidationException("Alokasi yang sudah selesai tidak dapat dibatalkan.");

        if (allocation.Status == AllocationStatus.Cancelled)
            throw new ValidationException("Alokasi ini sudah dibatalkan.");

        if (allocation.Status != AllocationStatus.Active)
            throw new ValidationException("Hanya alokasi aktif yang dapat dibatalkan.");

        allocation.Status = AllocationStatus.Cancelled;
        await _db.SaveChangesAsync(ct);

        return allocation;
    }

    public async Task<Allocation> CreateAllocationAsync(CreateAllocationCommand command, CancellationToken ct = default)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Allocation Amount must be greater than 0.");

        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Allocation Name is required.");

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.AccountId && a.ScopeId == command.ScopeId, ct);

        if (account is null)
            throw new ValidationException("Account not found in current Scope.");

        if (account.IsArchived)
            throw new ValidationException("Cannot create allocation on an archived Account.");

        var balance = await _db.TransactionEntries
            .Where(te => te.AccountId == command.AccountId)
            .SumAsync(te => te.Amount, ct);

        var activeAllocated = await _db.Allocations
            .Where(a => a.AccountId == command.AccountId && a.ScopeId == command.ScopeId && a.Status == AllocationStatus.Active)
            .SumAsync(a => a.Amount, ct);

        if (activeAllocated + command.Amount > balance)
        {
            var remaining = Math.Max(0, balance - activeAllocated);
            throw new ValidationException(
                $"Dana tidak mencukupi. Sisa alokasi tersedia: Rp{remaining:N0}.");
        }

        var allocation = new Allocation
        {
            ScopeId = command.ScopeId,
            AccountId = command.AccountId,
            Name = command.Name.Trim(),
            Amount = command.Amount,
            Status = AllocationStatus.Active
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

        if (command.Status is not null)
        {
            var newStatus = ParseStatus(command.Status);

            if (allocation.Status == AllocationStatus.Completed)
                throw new ValidationException("Allocation yang sudah selesai tidak dapat diubah.");

            if (allocation.Status == AllocationStatus.Cancelled)
                throw new ValidationException("Allocation yang sudah dibatalkan tidak dapat diubah.");

            if (allocation.Status == AllocationStatus.Active && newStatus != AllocationStatus.Active)
            {
                // Completing or cancelling — release reserved amount (no transaction needed)
                allocation.Status = newStatus;
            }
            else if (newStatus == AllocationStatus.Active && allocation.Status != AllocationStatus.Active)
            {
                // Reactivating — validate account is not archived and balance allows it
                var account = await _db.Accounts
                    .FirstOrDefaultAsync(a => a.Id == allocation.AccountId && a.ScopeId == command.ScopeId, ct);

                if (account is null)
                    throw new ValidationException("Account not found in current Scope.");

                if (account.IsArchived)
                    throw new ValidationException("Cannot reactivate allocation on an archived Account.");

                var balance = await _db.TransactionEntries
                    .Where(te => te.AccountId == allocation.AccountId)
                    .SumAsync(te => te.Amount, ct);

                var otherActive = await _db.Allocations
                    .Where(a => a.AccountId == allocation.AccountId
                        && a.ScopeId == command.ScopeId
                        && a.Status == AllocationStatus.Active
                        && a.Id != allocationId)
                    .SumAsync(a => a.Amount, ct);

                if (otherActive + allocation.Amount > balance)
                {
                    var remaining = Math.Max(0, balance - otherActive);
                    throw new ValidationException(
                        $"Dana tidak mencukupi untuk mengaktifkan kembali alokasi ini. Sisa alokasi tersedia: Rp{remaining:N0}.");
                }

                allocation.Status = AllocationStatus.Active;
            }
            else
            {
                allocation.Status = newStatus;
            }
        }

        if (command.Amount.HasValue)
        {
            if (command.Amount.Value <= 0)
                throw new ValidationException("Allocation Amount must be greater than 0.");

            if (allocation.Status == AllocationStatus.Active)
            {
                var balance = await _db.TransactionEntries
                    .Where(te => te.AccountId == allocation.AccountId)
                    .SumAsync(te => te.Amount, ct);

                var otherActive = await _db.Allocations
                    .Where(a => a.AccountId == allocation.AccountId
                        && a.ScopeId == command.ScopeId
                        && a.Status == AllocationStatus.Active
                        && a.Id != allocationId)
                    .SumAsync(a => a.Amount, ct);

                if (otherActive + command.Amount.Value > balance)
                {
                    var remaining = Math.Max(0, balance - otherActive);
                    throw new ValidationException(
                        $"Dana tidak mencukupi. Sisa alokasi tersedia: Rp{remaining:N0}.");
                }
            }

            allocation.Amount = command.Amount.Value;
        }

        if (command.AccountId.HasValue)
        {
            var newAccount = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Id == command.AccountId.Value && a.ScopeId == command.ScopeId, ct);

            if (newAccount is null)
                throw new ValidationException("Account not found in current Scope.");

            if (newAccount.IsArchived)
                throw new ValidationException("Cannot move allocation to an archived Account.");

            if (allocation.Status == AllocationStatus.Active)
            {
                var balance = await _db.TransactionEntries
                    .Where(te => te.AccountId == command.AccountId.Value)
                    .SumAsync(te => te.Amount, ct);

                var otherActive = await _db.Allocations
                    .Where(a => a.AccountId == command.AccountId.Value
                        && a.ScopeId == command.ScopeId
                        && a.Status == AllocationStatus.Active
                        && a.Id != allocationId)
                    .SumAsync(a => a.Amount, ct);

                if (otherActive + allocation.Amount > balance)
                {
                    var remaining = Math.Max(0, balance - otherActive);
                    throw new ValidationException(
                        $"Dana tidak mencukupi di akun '{newAccount.Name}'. Sisa alokasi tersedia: Rp{remaining:N0}.");
                }
            }

            allocation.AccountId = command.AccountId.Value;
        }

        await _db.SaveChangesAsync(ct);

        return allocation;
    }

    public async Task<decimal> GetAllocatedAmountAsync(Guid accountId, Guid scopeId, CancellationToken ct = default)
    {
        return await _db.Allocations
            .Where(a => a.AccountId == accountId && a.ScopeId == scopeId && a.Status == AllocationStatus.Active)
            .SumAsync(a => a.Amount, ct);
    }

    private static AllocationStatus ParseStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "active" => AllocationStatus.Active,
            "completed" => AllocationStatus.Completed,
            "cancelled" => AllocationStatus.Cancelled,
            _ => throw new ValidationException($"Status tidak valid: '{status}'. Gunakan 'active', 'completed', atau 'cancelled'.")
        };
    }
}
