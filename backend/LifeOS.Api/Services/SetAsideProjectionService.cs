using System.Linq.Expressions;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Read-only set-aside queries and Finance projections. Mutation and ledger rules
/// remain in <see cref="SetAsideService"/>.
/// </summary>
public sealed class SetAsideProjectionService(
    ApplicationDbContext db,
    BalanceCalculator balances)
{
    public Task<List<SetAsideProjection>> GetSetAsidesAsync(Guid scopeId, CancellationToken ct = default)
        => ProjectForScopeAsync(scopeId, filter: null, includeRecentEntries: false, ct);

    public async Task<SetAsideProjection?> GetSetAsideAsync(
        Guid setAsideId,
        Guid scopeId,
        CancellationToken ct = default)
    {
        var exists = await db.SetAsides.AnyAsync(sa => sa.Id == setAsideId && sa.ScopeId == scopeId, ct);
        if (!exists) return null;

        var result = await ProjectForScopeAsync(
            scopeId,
            sa => sa.Id == setAsideId,
            includeRecentEntries: true,
            ct);
        return result.SingleOrDefault();
    }

    public async Task<SetAsideHistoryPage> GetHistoryAsync(
        Guid setAsideId,
        Guid scopeId,
        string? cursor = null,
        int pageSize = 30,
        CancellationToken ct = default)
    {
        var exists = await db.SetAsides.AnyAsync(sa => sa.Id == setAsideId && sa.ScopeId == scopeId, ct);
        if (!exists)
            throw new ValidationException("Set-aside not found in current Scope.");

        pageSize = Math.Clamp(pageSize, 1, 100);
        DateTime? beforeCreatedAt = null;
        Guid? beforeId = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var parts = cursor.Split('|', 2);
            if (parts.Length != 2
                || !DateTime.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate)
                || !Guid.TryParse(parts[1], out var parsedId))
                throw new ValidationException("Cursor riwayat tidak valid.");
            beforeCreatedAt = parsedDate;
            beforeId = parsedId;
        }

        var query = db.SetAsideEntries.Where(se => se.SetAsideId == setAsideId);
        if (beforeCreatedAt.HasValue && beforeId.HasValue)
            query = query.Where(se => se.CreatedAt < beforeCreatedAt.Value
                || (se.CreatedAt == beforeCreatedAt.Value && se.Id.CompareTo(beforeId.Value) < 0));

        var rows = await query
            .OrderByDescending(se => se.CreatedAt)
            .ThenByDescending(se => se.Id)
            .Take(pageSize + 1)
            .Select(se => new SetAsideEntryProjection
            {
                Id = se.Id,
                Type = se.Type,
                Amount = se.Amount,
                TransactionId = se.TransactionId,
                Note = se.Note,
                CreatedAt = se.CreatedAt,
                Transaction = se.Transaction == null ? null : new SetAsideTransactionSummary
                {
                    Id = se.Transaction.Id,
                    Type = se.Transaction.Type,
                    Amount = se.Transaction.Amount,
                    Description = se.Transaction.Description,
                    CategoryName = se.Transaction.CategoryName,
                    OccurredOn = se.Transaction.OccurredOn,
                    RelatedDescription = se.Transaction.RelatedTransaction != null
                        ? se.Transaction.RelatedTransaction.Description
                        : null
                }
            })
            .ToListAsync(ct);

        // Include newer deltas omitted by the cursor so historical balances are
        // correct even when the requested page is older than the first page.
        var first = rows.FirstOrDefault();
        var newerBalanceDeltas = first is null
            ? 0m
            : await db.SetAsideEntries
                .Where(se => se.SetAsideId == setAsideId
                    && (se.CreatedAt > first.CreatedAt
                        || (se.CreatedAt == first.CreatedAt && se.Id.CompareTo(first.Id) > 0)))
                .SumAsync(se => (decimal?)se.Amount, ct) ?? 0m;
        var runningBalance = await balances.GetSetAsideAmountAsync(setAsideId, ct) - newerBalanceDeltas;
        foreach (var row in rows)
        {
            row.BalanceAfter = runningBalance;
            runningBalance -= row.Amount;
        }

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        return new SetAsideHistoryPage
        {
            Items = rows,
            HasMore = hasMore,
            NextCursor = hasMore && last is not null
                ? $"{last.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}|{last.Id:D}"
                : null
        };
    }

    internal async Task<List<SetAsideProjection>> ProjectForScopeAsync(
        Guid scopeId,
        Expression<Func<SetAside, bool>>? filter,
        bool includeRecentEntries,
        CancellationToken ct)
    {
        var scopeSetAsides = await db.SetAsides.Where(sa => sa.ScopeId == scopeId).ToListAsync(ct);
        if (scopeSetAsides.Count == 0) return [];

        var selected = filter is null
            ? scopeSetAsides
            : scopeSetAsides.AsQueryable().Where(filter).ToList();

        if (selected.Count == 0) return [];

        var today = BusinessDate.TodayWib;
        var allIds = scopeSetAsides.Select(sa => sa.Id).ToList();
        var amounts = await balances.GetSetAsideAmountsAsync(allIds, ct);

        // Legacy account names are projected for compatibility, not calculations.
        var legacyAccountIds = scopeSetAsides
            .Where(sa => sa.AccountId.HasValue)
            .Select(sa => sa.AccountId!.Value)
            .Distinct()
            .ToList();
        var defaultSourceIds = scopeSetAsides
            .Where(sa => sa.DefaultSourceAccountId.HasValue)
            .Select(sa => sa.DefaultSourceAccountId!.Value)
            .Distinct()
            .ToList();
        var lookupIds = legacyAccountIds.Concat(defaultSourceIds).Distinct().ToList();
        var accountNames = lookupIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Accounts
                .Where(a => lookupIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        // Usage rows and transaction amounts are filtered/aggregated in SQL to
        // avoid materializing the complete Spend ledger for the scope.
        var spendUsage = await ProjectSpendUsageAsync(selected, scopeId, today, ct);
        var usedBySetAside = spendUsage.ToDictionary(entry => entry.Key, entry => entry.Value.Amount);
        var executedBySetAside = selected.ToDictionary(
            setAside => setAside.Id,
            setAside =>
            {
                var (from, to) = SetAsideCycle.UsageWindow(setAside, today);
                return setAside.Kind == SetAsideKind.RoutineBatch
                    && from.HasValue
                    && to.HasValue
                    && spendUsage.TryGetValue(setAside.Id, out var usage)
                    && usage.HasTransactionSpend;
            });

        // Simulate cycle rollover funding against one shared FreeCash pool.
        var pendingFunding = new Dictionary<Guid, decimal>();
        var pendingSurplus = new Dictionary<Guid, decimal>();
        var pendingShortfall = new Dictionary<Guid, decimal>();
        var scopeAvailable = Math.Max(0m, await balances.GetScopeFreeCashAsync(scopeId, ct));

        foreach (var sa in scopeSetAsides
                     .Where(x => x.Status == SetAsideStatus.Active)
                     .OrderBy(x => x.CreatedAt))
        {
            if (!sa.TargetAmount.HasValue || !SetAsideCycle.HasCycle(sa.CycleKind))
            {
                pendingFunding[sa.Id] = 0m;
                pendingSurplus[sa.Id] = 0m;
                pendingShortfall[sa.Id] = 0m;
                continue;
            }

            if (!SetAsideCycle.IsRolloverPending(sa, today))
            {
                pendingFunding[sa.Id] = sa.CycleFundingShortfall;
                pendingSurplus[sa.Id] = 0m;
                pendingShortfall[sa.Id] = sa.CycleFundingShortfall;
                continue;
            }

            var current = amounts.GetValueOrDefault(sa.Id, 0m);
            var target = sa.TargetAmount.Value;
            if (current > target)
            {
                var surplus = current - target;
                pendingSurplus[sa.Id] = surplus;
                pendingFunding[sa.Id] = 0m;
                pendingShortfall[sa.Id] = 0m;
                scopeAvailable += surplus;
            }
            else if (current < target)
            {
                var required = target - current;
                var applied = Math.Min(required, scopeAvailable);
                pendingFunding[sa.Id] = required;
                pendingSurplus[sa.Id] = 0m;
                pendingShortfall[sa.Id] = required - applied;
                scopeAvailable -= applied;
            }
            else
            {
                pendingFunding[sa.Id] = 0m;
                pendingSurplus[sa.Id] = 0m;
                pendingShortfall[sa.Id] = 0m;
            }
        }

        Dictionary<Guid, List<SetAsideEntryProjection>>? recentBySetAside = null;
        if (includeRecentEntries)
        {
            var selectedIds = selected.Select(sa => sa.Id).ToList();
            var entries = await db.SetAsideEntries
                .Where(se => selectedIds.Contains(se.SetAsideId))
                .OrderByDescending(se => se.CreatedAt)
                .ThenByDescending(se => se.Id)
                .Take(20)
                .Select(se => new SetAsideEntryProjection
                {
                    Id = se.Id,
                    Type = se.Type,
                    Amount = se.Amount,
                    TransactionId = se.TransactionId,
                    Note = se.Note,
                    CreatedAt = se.CreatedAt,
                    Transaction = se.Transaction == null ? null : new SetAsideTransactionSummary
                    {
                        Id = se.Transaction.Id,
                        Type = se.Transaction.Type,
                        Amount = se.Transaction.Amount,
                        Description = se.Transaction.Description,
                        CategoryName = se.Transaction.CategoryName,
                        OccurredOn = se.Transaction.OccurredOn,
                        RelatedDescription = se.Transaction.RelatedTransaction != null
                            ? se.Transaction.RelatedTransaction.Description
                            : null
                    }
                })
                .ToListAsync(ct);

            recentBySetAside = entries.GroupBy(entry => selected[0].Id)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        return selected
            .OrderByDescending(sa => sa.CreatedAt)
            .Select(sa =>
            {
                var current = amounts.GetValueOrDefault(sa.Id, 0m);
                var rolloverPending = SetAsideCycle.IsRolloverPending(sa, today);
                var (cycleStart, cycleEnd) = SetAsideCycle.UsageWindow(sa, today);
                var shortfall = pendingShortfall.GetValueOrDefault(sa.Id, 0m);

                return new SetAsideProjection
                {
                    Id = sa.Id,
                    AccountId = sa.AccountId,
                    AccountName = sa.AccountId.HasValue
                        ? accountNames.GetValueOrDefault(sa.AccountId.Value, "")
                        : null,
                    DefaultSourceAccountId = sa.DefaultSourceAccountId,
                    DefaultSourceAccountName = sa.DefaultSourceAccountId.HasValue
                        ? accountNames.GetValueOrDefault(sa.DefaultSourceAccountId.Value, "")
                        : null,
                    Name = sa.Name,
                    Kind = sa.Kind,
                    Note = sa.Note,
                    TransactionCategory = sa.TransactionCategory,
                    Amount = current,
                    TargetAmount = sa.TargetAmount,
                    TargetShortfall = sa.TargetAmount.HasValue
                        ? Math.Max(0m, sa.TargetAmount.Value - current)
                        : 0m,
                    CycleKind = sa.CycleKind,
                    CycleAnchorDate = sa.CycleAnchorDate,
                    CurrentCycleStart = SetAsideCycle.HasCycle(sa.CycleKind) ? cycleStart : null,
                    CurrentCycleEnd = SetAsideCycle.HasCycle(sa.CycleKind) ? cycleEnd : null,
                    IsCycleRolloverPending = rolloverPending,
                    CycleFundingRequired = pendingFunding.GetValueOrDefault(sa.Id, 0m),
                    CycleSurplus = pendingSurplus.GetValueOrDefault(sa.Id, 0m),
                    CycleFundingShortfall = shortfall,
                    IsUnderfunded = shortfall > 0,
                    UsedAmount = usedBySetAside.GetValueOrDefault(sa.Id, 0m),
                    IsCycleExecuted = executedBySetAside.GetValueOrDefault(sa.Id),
                    Status = sa.Status,
                    CloseReason = sa.CloseReason,
                    CreatedAt = sa.CreatedAt,
                    UpdatedAt = sa.UpdatedAt,
                    RecentEntries = recentBySetAside?.GetValueOrDefault(sa.Id, []) ?? []
                };
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, SetAsideSpendUsage>> ProjectSpendUsageAsync(
        IReadOnlyList<SetAside> selected,
        Guid scopeId,
        DateOnly today,
        CancellationToken ct)
    {
        var windowGroups = selected
            .Select(setAside =>
            {
                var (from, to) = SetAsideCycle.UsageWindow(setAside, today);
                return new
                {
                    SetAsideId = setAside.Id,
                    FromUtc = from.HasValue ? BusinessDate.StartOfDayUtc(from.Value) : (DateTime?)null,
                    ToExclusiveUtc = to.HasValue
                        ? BusinessDate.StartOfDayUtc(to.Value.AddDays(1))
                        : (DateTime?)null
                };
            })
            .GroupBy(window => (window.FromUtc, window.ToExclusiveUtc))
            .Select(group => new SetAsideSpendWindow(
                group.Select(window => window.SetAsideId).ToArray(),
                group.Key.FromUtc,
                group.Key.ToExclusiveUtc))
            .ToArray();

        var usagePredicate = BuildSpendWindowPredicate(windowGroups);
        var relevantSpendEntries = db.SetAsideEntries
            .Where(usagePredicate)
            .Where(entry => entry.Type == SetAsideEntryType.Spent);
        var relevantTransactionIds = relevantSpendEntries
            .Where(entry => entry.TransactionId.HasValue)
            .Select(entry => entry.TransactionId!.Value)
            .Distinct();
        var transactionSpend = db.TransactionEntries
            .Where(entry => relevantTransactionIds.Contains(entry.TransactionId))
            .GroupBy(entry => entry.TransactionId)
            .Select(group => new
            {
                TransactionId = group.Key,
                Amount = -group.Sum(entry => entry.Amount)
            });

        var usageQuery =
            from entry in relevantSpendEntries
            join spend in transactionSpend on entry.TransactionId equals (Guid?)spend.TransactionId into spends
            from spend in spends.DefaultIfEmpty()
            where !entry.TransactionId.HasValue
                || !db.Transactions.Any(reversal =>
                    reversal.ScopeId == scopeId
                    && reversal.Type == TransactionType.Reversal
                    && reversal.RelatedTransactionId == entry.TransactionId)
            group new
            {
                entry.TransactionId,
                entry.Amount,
                TransactionAmount = spend == null ? (decimal?)null : spend.Amount
            }
            by entry.SetAsideId
            into entries
            select new
            {
                SetAsideId = entries.Key,
                Amount = entries.Sum(item => item.TransactionId.HasValue
                    ? item.TransactionAmount ?? -item.Amount
                    : -item.Amount),
                HasTransactionSpend = entries.Any(item => item.TransactionId.HasValue)
            };

        return await usageQuery.ToDictionaryAsync(
            item => item.SetAsideId,
            item => new SetAsideSpendUsage(item.Amount, item.HasTransactionSpend),
            ct);
    }

    private static Expression<Func<SetAsideEntry, bool>> BuildSpendWindowPredicate(
        IReadOnlyList<SetAsideSpendWindow> windows)
    {
        var entry = Expression.Parameter(typeof(SetAsideEntry), "entry");
        var setAsideId = Expression.Property(entry, nameof(SetAsideEntry.SetAsideId));
        var createdAt = Expression.Property(entry, nameof(SetAsideEntry.CreatedAt));
        Expression predicate = Expression.Constant(false);

        foreach (var window in windows)
        {
            var ids = Expression.Call(
                typeof(EF),
                nameof(EF.Parameter),
                [typeof(Guid[])],
                Expression.Constant(window.SetAsideIds));
            var containsId = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                [typeof(Guid)],
                ids,
                setAsideId);
            Expression clause = containsId;
            if (window.FromUtc.HasValue)
            {
                clause = Expression.AndAlso(clause,
                    Expression.GreaterThanOrEqual(createdAt, Expression.Constant(window.FromUtc.Value)));
            }
            if (window.ToExclusiveUtc.HasValue)
            {
                clause = Expression.AndAlso(clause,
                    Expression.LessThan(createdAt, Expression.Constant(window.ToExclusiveUtc.Value)));
            }
            predicate = Expression.OrElse(predicate, clause);
        }

        return Expression.Lambda<Func<SetAsideEntry, bool>>(predicate, entry);
    }

    private sealed record SetAsideSpendWindow(Guid[] SetAsideIds, DateTime? FromUtc, DateTime? ToExclusiveUtc);
    private sealed record SetAsideSpendUsage(decimal Amount, bool HasTransactionSpend);
}
