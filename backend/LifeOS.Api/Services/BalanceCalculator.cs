using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Single source of truth for derived money figures.
///
///   actual balance = SUM(TransactionEntry.Amount)
///   set-aside      = SUM(SetAsideEntry.Amount) over Active set-asides
///   available      = actual balance - set-aside
///
/// Nothing here is persisted. Every figure is derived from the ledger.
/// </summary>
public class BalanceCalculator
{
    private readonly ApplicationDbContext _db;

    public BalanceCalculator(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetActualBalanceAsync(Guid accountId, CancellationToken ct = default)
        => await _db.TransactionEntries
            .Where(te => te.AccountId == accountId)
            .SumAsync(te => (decimal?)te.Amount, ct) ?? 0m;

    public async Task<decimal> GetActiveSetAsideAsync(Guid accountId, CancellationToken ct = default)
        => await _db.SetAsideEntries
            .Where(se => se.SetAside.AccountId == accountId && se.SetAside.Status == SetAsideStatus.Active)
            .SumAsync(se => (decimal?)se.Amount, ct) ?? 0m;

    public async Task<decimal> GetAvailableAsync(Guid accountId, CancellationToken ct = default)
        => await GetActualBalanceAsync(accountId, ct) - await GetActiveSetAsideAsync(accountId, ct);

    public async Task<Dictionary<Guid, decimal>> GetActualBalancesAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
    {
        if (accountIds.Count == 0) return [];

        return await _db.TransactionEntries
            .Where(te => accountIds.Contains(te.AccountId))
            .GroupBy(te => te.AccountId)
            .Select(g => new { g.Key, Balance = g.Sum(te => te.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Balance, ct);
    }

    public async Task<Dictionary<Guid, decimal>> GetActiveSetAsidesAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
    {
        if (accountIds.Count == 0) return [];

        return await _db.SetAsideEntries
            .Where(se => accountIds.Contains(se.SetAside.AccountId)
                && se.SetAside.Status == SetAsideStatus.Active)
            .GroupBy(se => se.SetAside.AccountId)
            .Select(g => new { g.Key, Reserved = g.Sum(se => se.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Reserved, ct);
    }

    /// <summary>Current reserved amount of a single SetAside (SUM of its history).</summary>
    public async Task<decimal> GetSetAsideAmountAsync(Guid setAsideId, CancellationToken ct = default)
        => await _db.SetAsideEntries
            .Where(se => se.SetAsideId == setAsideId)
            .SumAsync(se => (decimal?)se.Amount, ct) ?? 0m;

    /// <summary>Current reserved amount per SetAside id.</summary>
    public async Task<Dictionary<Guid, decimal>> GetSetAsideAmountsAsync(
        IReadOnlyCollection<Guid> setAsideIds, CancellationToken ct = default)
    {
        if (setAsideIds.Count == 0) return [];

        return await _db.SetAsideEntries
            .Where(se => setAsideIds.Contains(se.SetAsideId))
            .GroupBy(se => se.SetAsideId)
            .Select(g => new { g.Key, Reserved = g.Sum(se => se.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Reserved, ct);
    }
}
