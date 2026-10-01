using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Satu lapisan query yang menjadi sumber perhitungan Financial State,
/// supaya FE tidak membuat rumusnya sendiri.
///
/// Read ini murni: tidak pernah menulis ke database. Normalisasi cycle yang belum
/// dijalankan hanya dihitung sebagai angka turunan (PendingCycleFunding/Surplus) dan
/// baru benar-benar diterapkan saat ada command yang relevan.
/// </summary>
public class FinanceStateService
{
    private readonly ApplicationDbContext _db;
    private readonly BalanceCalculator _balances;
    private readonly SetAsideService _setAsides;
    private readonly TransactionService _transactions;

    public FinanceStateService(
        ApplicationDbContext db,
        BalanceCalculator balances,
        SetAsideService setAsides,
        TransactionService transactions)
    {
        _db = db;
        _balances = balances;
        _setAsides = setAsides;
        _transactions = transactions;
    }

    public async Task<FinanceStateProjection> GetStateAsync(Guid scopeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var accounts = await _db.Accounts
            .Where(a => a.ScopeId == scopeId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var accountIds = accounts.Select(a => a.Id).ToList();
        var actuals = await _balances.GetActualBalancesAsync(accountIds, ct);
        var reserved = await _balances.GetActiveSetAsidesAsync(accountIds, ct);

        var setAsideProjections = await _setAsides.GetSetAsidesAsync(scopeId, ct);

        var pendingFundingByAccount = setAsideProjections
            .Where(sa => sa.Status == SetAsideStatus.Active)
            .GroupBy(sa => sa.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(sa => sa.CycleFundingRequired));
        var pendingSurplusByAccount = setAsideProjections
            .Where(sa => sa.Status == SetAsideStatus.Active)
            .GroupBy(sa => sa.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(sa => sa.CycleSurplus));

        var accountProjections = accounts.Select(a =>
        {
            var actual = actuals.GetValueOrDefault(a.Id, 0m);
            var setAside = reserved.GetValueOrDefault(a.Id, 0m);
            return new AccountStateProjection
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                IsArchived = a.IsArchived,
                ActualBalance = actual,
                SetAsideAmount = setAside,
                AvailableBalance = actual - setAside,
                PendingCycleFunding = pendingFundingByAccount.GetValueOrDefault(a.Id, 0m),
                PendingCycleSurplus = pendingSurplusByAccount.GetValueOrDefault(a.Id, 0m),
                CreatedAt = a.CreatedAt
            };
        }).ToList();

        // Agenda yang masih direncanakan. Agenda yang diharapkan TIDAK PERNAH
        // mengubah actual balance, jadi ia hanya muncul sebagai kewajiban turunan.
        var events = await _db.UpcomingEvents
            .Where(ue => ue.ScopeId == scopeId)
            .ToListAsync(ct);

        var eventAccountIds = events.Where(e => e.AccountId.HasValue).Select(e => e.AccountId!.Value).Distinct().ToList();
        var eventAccountNames = eventAccountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts
                .Where(a => eventAccountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var scheduledEvents = events
            .Where(e => e.Status == UpcomingEventStatus.Scheduled)
            .Select(e => UpcomingEventService.Project(
                e, e.AccountId is null ? null : eventAccountNames.GetValueOrDefault(e.AccountId.Value), today))
            .OrderBy(e => e.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();

        var dueEvents = scheduledEvents.Where(e => e.IsDue).ToList();

        var dueObligations = dueEvents
            .Where(e => e.Direction == UpcomingEventDirection.Expense)
            .Sum(e => e.Amount);

        var totalActual = accountProjections.Sum(a => a.ActualBalance);
        var totalSetAside = accountProjections.Sum(a => a.SetAsideAmount);
        var pendingFunding = accountProjections.Sum(a => a.PendingCycleFunding);
        var pendingSurplus = accountProjections.Sum(a => a.PendingCycleSurplus);
        var totalCommitted = totalSetAside + pendingFunding - pendingSurplus;
        var freeCash = totalActual - totalCommitted - dueObligations;

        var recentTransactions = await _transactions.GetTransactionsAsync(scopeId, limit: 20, ct: ct);

        return new FinanceStateProjection
        {
            Today = today,
            TotalActualBalance = totalActual,
            TotalSetAside = totalSetAside,
            PendingCycleFunding = pendingFunding,
            PendingCycleSurplus = pendingSurplus,
            TotalCommittedSetAside = totalCommitted,
            TotalAvailable = totalActual - totalSetAside,
            DueObligations = dueObligations,
            DueObligationsCount = dueEvents.Count(e => e.Direction == UpcomingEventDirection.Expense),
            OverdueObligationsCount = dueEvents.Count(e => e.Direction == UpcomingEventDirection.Expense && e.IsOverdue),
            FreeCash = freeCash,
            HasUnpaidBills = dueEvents.Any(e => e.Direction == UpcomingEventDirection.Expense),
            AllBillsPaid = events.Any(e => e.Direction == UpcomingEventDirection.Expense)
                && !dueEvents.Any(e => e.Direction == UpcomingEventDirection.Expense),
            Accounts = accountProjections,
            SetAsides = setAsideProjections,
            UpcomingEvents = scheduledEvents,
            DueEvents = dueEvents,
            RecentTransactions = recentTransactions
        };
    }
}
