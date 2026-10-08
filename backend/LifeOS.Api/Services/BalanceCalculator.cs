using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Single source of truth for derived money figures.
///
///   actual balance (per account) = SUM(TransactionEntry.Amount) where AccountId = X
///   set-aside (scope-wide)       = SUM(SetAsideEntry.Amount) over Active set-asides
///
/// SET-ASIDE TIDAK PERNAH DIHITUNG PER AKUN. <see cref="SetAside.AccountId"/> legacy
/// tidak dipakai di method ini sama sekali. Alokasi adalah pool scope-wide.
///
/// Turunan scope-wide:
///   TotalAvailable = TotalActual − TotalSetAside
///     (uang yang belum dialokasikan; sebelum komitmen Rencana)
///   FreeCash (Uang Bebas) = TotalActual − TotalSetAside − ScheduledExpenseCommitments
///     Unfunded cycle targets remain visible as shortfall but do not reserve unavailable cash.
///
/// FreeCash dan TotalAvailable adalah dua konsep terpisah:
///   - TotalAvailable = uang yang belum masuk pos (belum dialokasikan).
///   - FreeCash = uang yang belum dialokasikan dan belum terikat agenda terjadwal.
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

    // ─────────────── Per account (Sumber Dana = lokasi uang) ───────────────

    public async Task<decimal> GetActualBalanceAsync(Guid accountId, CancellationToken ct = default)
        => await _db.TransactionEntries
            .Where(te => te.AccountId == accountId)
            .SumAsync(te => (decimal?)te.Amount, ct) ?? 0m;

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

    // ─────────────── Set-aside (Dana yang Disisihkan = alokasi scope-wide) ───────────────

    /// <summary>Σ saldo disisihkan aktif untuk seluruh scope (bukan per akun).</summary>
    public async Task<decimal> GetActiveSetAsideTotalAsync(Guid scopeId, CancellationToken ct = default)
        => await _db.SetAsideEntries
            .Where(se => se.SetAside.ScopeId == scopeId && se.SetAside.Status == SetAsideStatus.Active)
            .SumAsync(se => (decimal?)se.Amount, ct) ?? 0m;

    /// <summary>Saldo yang sedang disisihkan pada satu SetAside (SUM history).</summary>
    public async Task<decimal> GetSetAsideAmountAsync(Guid setAsideId, CancellationToken ct = default)
        => await _db.SetAsideEntries
            .Where(se => se.SetAsideId == setAsideId)
            .SumAsync(se => (decimal?)se.Amount, ct) ?? 0m;

    /// <summary>Saldo yang sedang disisihkan per SetAside id.</summary>
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

    // ─────────────── Scope-wide totals ───────────────

    /// <summary>Σ saldo aktual seluruh akun dalam scope.</summary>
    public async Task<decimal> GetScopeActualTotalAsync(Guid scopeId, CancellationToken ct = default)
        => await _db.TransactionEntries
            .Where(te => te.Account.ScopeId == scopeId)
            .SumAsync(te => (decimal?)te.Amount, ct) ?? 0m;

    /// <summary>TotalAvailable scope = TotalActual − TotalSetAside.</summary>
    public async Task<decimal> GetScopeAvailableAsync(Guid scopeId, CancellationToken ct = default)
    {
        var totalActual = await _db.TransactionEntries
            .Where(te => te.Account.ScopeId == scopeId)
            .SumAsync(te => (decimal?)te.Amount, ct) ?? 0m;

        var totalSetAside = await GetActiveSetAsideTotalAsync(scopeId, ct);

        return totalActual - totalSetAside;
    }

    /// <summary>
    /// Actual cash not allocated to set-asides or scheduled expense commitments.
    /// Unfunded cycle targets are informational and do not reduce this amount.
    /// </summary>
    public async Task<decimal> GetScopeFreeCashAsync(Guid scopeId, CancellationToken ct = default)
    {
        var totalActual = await GetScopeActualTotalAsync(scopeId, ct);
        var totalSetAside = await GetActiveSetAsideTotalAsync(scopeId, ct);
        var scheduledExpenses = await _db.UpcomingEvents
            .Where(e => e.ScopeId == scopeId
                && e.Status == UpcomingEventStatus.Scheduled
                && e.Direction == UpcomingEventDirection.Expense)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var activeCycles = await _db.SetAsides
            .Where(sa => sa.ScopeId == scopeId
                && sa.Status == SetAsideStatus.Active
                && sa.TargetAmount.HasValue
                && sa.CycleKind != SetAsideCycleKind.None)
            .ToListAsync(ct);
        var cycleSurplus = 0m;
        var activeCycleIds = activeCycles.Select(sa => sa.Id).ToList();
        var cycleAmounts = await GetSetAsideAmountsAsync(activeCycleIds, ct);
        var today = BusinessDate.TodayWib;
        foreach (var setAside in activeCycles.Where(sa => SetAsideCycle.IsRolloverPending(sa, today)))
        {
            var current = cycleAmounts.GetValueOrDefault(setAside.Id, 0m);
            cycleSurplus += Math.Max(0m, current - setAside.TargetAmount!.Value);
        }

        return totalActual - totalSetAside + cycleSurplus - scheduledExpenses;
    }
}
