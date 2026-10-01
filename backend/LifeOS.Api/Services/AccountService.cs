using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public class AccountService
{
    private readonly ApplicationDbContext _db;
    private readonly BalanceCalculator _balances;

    public AccountService(ApplicationDbContext db, BalanceCalculator balances)
    {
        _db = db;
        _balances = balances;
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
                TotalActualBalance = 0,
                TotalSetAsideAmount = 0,
                TotalAvailableBalance = 0
            };
        }

        var accountIds = accounts.Select(a => a.Id).ToList();
        var balances = await _balances.GetActualBalancesAsync(accountIds, ct);
        var setAsides = await _balances.GetActiveSetAsidesAsync(accountIds, ct);

        var projections = accounts.Select(a =>
        {
            var actual = balances.GetValueOrDefault(a.Id, 0m);
            var reserved = setAsides.GetValueOrDefault(a.Id, 0m);
            return new AccountProjection
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                IsArchived = a.IsArchived,
                ActualBalance = actual,
                SetAsideAmount = reserved,
                AvailableBalance = actual - reserved,
                CreatedAt = a.CreatedAt
            };
        }).ToList();

        return new AccountListProjection
        {
            Accounts = projections,
            TotalActualBalance = projections.Sum(p => p.ActualBalance),
            TotalSetAsideAmount = projections.Sum(p => p.SetAsideAmount),
            TotalAvailableBalance = projections.Sum(p => p.AvailableBalance)
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

        var actual = await _balances.GetActualBalanceAsync(accountId, ct);
        var reserved = await _balances.GetActiveSetAsideAsync(accountId, ct);

        return new AccountProjection
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            IsArchived = account.IsArchived,
            ActualBalance = actual,
            SetAsideAmount = reserved,
            AvailableBalance = actual - reserved,
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
                // Archive validation: balance must be 0 AND no active set-asides
                var balance = await _balances.GetActualBalanceAsync(accountId, ct);

                if (balance != 0)
                    throw new ValidationException(
                        $"Akun belum bisa diarsipkan karena saldonya masih Rp{balance:N0}. Kosongkan saldo terlebih dahulu.");

                var activeSetAside = await _balances.GetActiveSetAsideAsync(accountId, ct);

                if (activeSetAside != 0)
                    throw new ValidationException(
                        $"Akun belum bisa diarsipkan karena masih ada uang yang disisihkan sebesar Rp{activeSetAside:N0}. Tarik atau tutup pos terlebih dahulu.");
            }

            account.IsArchived = command.IsArchived.Value;
        }

        await _db.SaveChangesAsync(ct);

        return account;
    }
}
