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
///
/// DUA ANGKA TERPISAH yang tidak boleh disamakan:
///   TotalAvailable = TotalActual − TotalSetAside
///     Uang yang belum dialokasikan ke Dana yang Disisihkan (sebelum komitmen Rencana).
///   FreeCash (Uang Bebas) = TotalActual − TotalSetAside + PendingCycleSurplus
///                           − ScheduledExpenseCommitments
///     Cycle shortfall yang belum terdanai tidak mengurangi FreeCash.
///     Uang yang benar-benar bebas dibelanjakan setelah semua komitmen.
///
/// SetAside tidak terikat Sumber Dana — total dihitung scope-wide.
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
        var today = BusinessDate.TodayWib;

        var accounts = await _db.Accounts
            .Where(a => a.ScopeId == scopeId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var accountIds = accounts.Select(a => a.Id).ToList();
        var actuals = await _balances.GetActualBalancesAsync(accountIds, ct);

        var setAsideProjections = await _setAsides.GetSetAsidesAsync(scopeId, ct);

        // TotalSetAside dihitung SCOPE-WIDE dari ledger pos — bukan per akun.
        // SetAside tidak terikat Sumber Dana.
        var totalSetAside = setAsideProjections
            .Where(sa => sa.Status == SetAsideStatus.Active)
            .Sum(sa => sa.Amount);

        // Pending cycle funding/surplus juga scope-wide (dihitung di ProjectForScopeAsync).
        var pendingFunding = setAsideProjections
            .Where(sa => sa.Status == SetAsideStatus.Active)
            .Sum(sa => sa.CycleFundingRequired);
        var pendingSurplus = setAsideProjections
            .Where(sa => sa.Status == SetAsideStatus.Active)
            .Sum(sa => sa.CycleSurplus);

        // Per akun: saldo aktual saja. Alokasi tidak mengurangi saldo per akun.
        var accountProjections = accounts.Select(a =>
        {
            var actual = actuals.GetValueOrDefault(a.Id, 0m);
            return new AccountStateProjection
            {
                Id = a.Id,
                Name = a.Name,
                Type = a.Type,
                IsArchived = a.IsArchived,
                ActualBalance = actual,
                // SetAsideAmount di 0 — alokasi scope-wide, bukan milik akun tertentu.
                SetAsideAmount = 0m,
                // AvailableBalance = saldo aktual — uang di akun ini bisa dipakai
                // dari akun ini. Alokasi dihitung di TotalAvailable scope-wide.
                AvailableBalance = actual,
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

        // SEMUA agenda pengeluaran terjadwal (sekali jalan & ber-siklus) mengikat
        // FreeCash sejak dibuat — mirip komitmen Pos, derived saja, tanpa transaksi.
        // Yang sedang jatuh tempo tidak dihitung dua kali karena komitmennya sudah
        // masuk di sini sejak awal. DueObligations tetap untuk tampilan Jatuh Tempo.
        var scheduledExpenseCommitments = scheduledEvents
            .Where(e => e.Direction == UpcomingEventDirection.Expense)
            .Sum(e => e.Amount);

        var totalActual = accountProjections.Sum(a => a.ActualBalance);
        var totalCommitted = totalSetAside - pendingSurplus;

        // DUA ANGKA TERPISAH — jangan disamakan:
        // TotalAvailable = uang yang belum dialokasikan (TotalActual − TotalSetAside).
        // Shortfall target siklus tidak mengurangi FreeCash; hanya dana yang sudah
        // dialokasikan dan agenda pengeluaran terjadwal yang menjadi komitmen.
        var totalAvailable = totalActual - totalSetAside;
        var freeCash = totalActual - totalCommitted - scheduledExpenseCommitments;

        var recentTransactions = await _transactions.GetTransactionsAsync(scopeId, limit: 20, ct: ct);

        return new FinanceStateProjection
        {
            Today = today,
            TotalActualBalance = totalActual,
            TotalSetAside = totalSetAside,
            PendingCycleFunding = pendingFunding,
            PendingCycleSurplus = pendingSurplus,
            TotalCommittedSetAside = totalCommitted,
            TotalAvailable = totalAvailable,
            DueObligations = dueObligations,
            DueObligationsCount = dueEvents.Count(e => e.Direction == UpcomingEventDirection.Expense),
            OverdueObligationsCount = dueEvents.Count(e => e.Direction == UpcomingEventDirection.Expense && e.IsOverdue),
            ScheduledExpenseCommitments = scheduledExpenseCommitments,
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
