using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public class AccountService
{
    private readonly ApplicationDbContext _db;

    public AccountService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Account> CreateAccountAsync(CreateAccountCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException("Account Name is required.");

        var trimmedName = command.Name.Trim();
        if (trimmedName.Length > 50)
            throw new ValidationException("Account Name must not exceed 50 characters.");

        if (!Enum.IsDefined(typeof(AccountType), command.Type))
            throw new ValidationException($"Invalid AccountType: {command.Type}");

        // Validate Scope exists
        var scopeExists = await _db.Scopes.AnyAsync(s => s.Id == command.ScopeId, ct);
        if (!scopeExists)
            throw new ValidationException("Scope not found.");

        var duplicateExists = await _db.Accounts.AnyAsync(
            a => a.ScopeId == command.ScopeId && a.Name.ToLower() == trimmedName.ToLower(), ct);
        if (duplicateExists)
            throw new ValidationException($"Akun dengan nama '{trimmedName}' sudah ada dalam scope ini.");

        var account = new Account
        {
            ScopeId = command.ScopeId,
            Name = trimmedName,
            Type = command.Type,
            IsArchived = false
        };

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);

        return account;
    }

    public async Task<AccountListProjection> GetAccountsAsync(
        Guid scopeId,
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        var query = _db.Accounts
            .Where(a => a.ScopeId == scopeId);

        if (!includeArchived)
            query = query.Where(a => !a.IsArchived);

        var accounts = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            return new AccountListProjection
            {
                Accounts = [],
                TotalBalance = 0,
                TotalAllocated = 0,
                TotalAvailable = 0
            };
        }

        var accountIds = accounts.Select(a => a.Id).ToList();

        // Efficient EF Core grouping for balance calculation
        var balances = await _db.TransactionEntries
            .Where(te => accountIds.Contains(te.AccountId))
            .GroupBy(te => te.AccountId)
            .Select(g => new { AccountId = g.Key, Balance = g.Sum(te => te.Amount) })
            .ToListAsync(ct);

        // Efficient EF Core grouping for allocated calculation
        var allocated = await _db.Allocations
            .Where(a => accountIds.Contains(a.AccountId) && a.IsActive)
            .GroupBy(a => a.AccountId)
            .Select(g => new { AccountId = g.Key, Allocated = g.Sum(a => a.Amount) })
            .ToListAsync(ct);

        var balanceDict = balances.ToDictionary(b => b.AccountId, b => b.Balance);
        var allocatedDict = allocated.ToDictionary(a => a.AccountId, a => a.Allocated);

        var projections = accounts.Select(a =>
        {
            var balance = balanceDict.GetValueOrDefault(a.Id, 0m);
            var alloc = allocatedDict.GetValueOrDefault(a.Id, 0m);
            return new AccountProjection
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                IsArchived = a.IsArchived,
                Balance = balance,
                Allocated = alloc,
                Available = balance - alloc,
                CreatedAt = a.CreatedAt
            };
        }).ToList();

        return new AccountListProjection
        {
            Accounts = projections,
            TotalBalance = projections.Sum(p => p.Balance),
            TotalAllocated = projections.Sum(p => p.Allocated),
            TotalAvailable = projections.Sum(p => p.Available)
        };
    }

    public async Task<AccountProjection> GetAccountByIdAsync(
        Guid accountId,
        Guid scopeId,
        CancellationToken ct = default)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.ScopeId == scopeId, ct);

        if (account is null)
            throw new ValidationException("Account not found in current Scope.");

        var balance = await _db.TransactionEntries
            .Where(te => te.AccountId == accountId)
            .SumAsync(te => te.Amount, ct);

        var allocated = await _db.Allocations
            .Where(a => a.AccountId == accountId && a.IsActive)
            .SumAsync(a => a.Amount, ct);

        return new AccountProjection
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            IsArchived = account.IsArchived,
            Balance = balance,
            Allocated = allocated,
            Available = balance - allocated,
            CreatedAt = account.CreatedAt
        };
    }

    public async Task<Account> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountCommand command,
        CancellationToken ct = default)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.ScopeId == command.ScopeId, ct);

        if (account is null)
            throw new ValidationException("Account not found in current Scope.");

        if (command.Name is not null)
        {
            var trimmedName = command.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
                throw new ValidationException("Account Name cannot be empty.");
            if (trimmedName.Length > 50)
                throw new ValidationException("Account Name must not exceed 50 characters.");

            var duplicateExists = await _db.Accounts.AnyAsync(
                a => a.ScopeId == command.ScopeId
                    && a.Id != accountId
                    && a.Name.ToLower() == trimmedName.ToLower(), ct);
            if (duplicateExists)
                throw new ValidationException($"Akun dengan nama '{trimmedName}' sudah ada dalam scope ini.");

            account.Name = trimmedName;
        }

        if (command.Type.HasValue)
        {
            if (!Enum.IsDefined(typeof(AccountType), command.Type.Value))
                throw new ValidationException($"Invalid AccountType: {command.Type.Value}");

            account.Type = command.Type.Value;
        }

        if (command.IsArchived.HasValue)
        {
            if (command.IsArchived.Value)
            {
                // Archive validation: balance must be 0 AND no active allocations
                var balance = await _db.TransactionEntries
                    .Where(te => te.AccountId == accountId)
                    .SumAsync(te => te.Amount, ct);

                if (balance != 0)
                    throw new ValidationException(
                        $"Akun belum bisa diarsipkan karena saldonya masih Rp{balance:N0}. Kosongkan saldo terlebih dahulu.");

                var activeAllocated = await _db.Allocations
                    .Where(a => a.AccountId == accountId && a.IsActive)
                    .SumAsync(a => a.Amount, ct);

                if (activeAllocated != 0)
                    throw new ValidationException(
                        $"Akun belum bisa diarsipkan karena masih ada alokasi aktif sebesar Rp{activeAllocated:N0}. Hapus atau pindahkan alokasi terlebih dahulu.");
            }

            account.IsArchived = command.IsArchived.Value;
        }

        await _db.SaveChangesAsync(ct);

        return account;
    }
}
