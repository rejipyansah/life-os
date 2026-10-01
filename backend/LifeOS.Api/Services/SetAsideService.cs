using System.Linq.Expressions;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Uang yang Disisihkan (set-aside).
///
/// A set-aside is a reservation/intention, NOT an expense:
///   - it never creates a TransactionEntry
///   - it never changes the actual account balance
///   - it only changes the derived available balance
///
/// The only operation that produces real money movement is <see cref="SpendAsync"/>,
/// which records the actual expense AND releases the reservation together.
///
/// Every mutation runs inside a SERIALIZABLE transaction so concurrent requests cannot
/// over-reserve, double-withdraw, or spend against a stale available balance.
///
/// Cycle normalization is applied on every write command that touches a set-aside
/// (create/update/add/withdraw/spend/close). A GET never writes.
///
/// The current reserved amount of a set-aside is derived from its history:
///   amount = SUM(SetAsideEntry.Amount)
/// </summary>
public class SetAsideService
{
    private readonly ApplicationDbContext _db;
    private readonly BalanceCalculator _balances;

    public SetAsideService(ApplicationDbContext db, BalanceCalculator balances)
    {
        _db = db;
        _balances = balances;
    }

    // ───────────────────────── Create / Update ─────────────────────────

    public Task<SetAside> CreateSetAsideAsync(CreateSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => CreateInternalAsync(command, token), ct);

    private async Task<SetAside> CreateInternalAsync(CreateSetAsideCommand command, CancellationToken ct)
    {
        var name = NormalizeName(command.Name);
        var account = await RequireAccountAsync(command.AccountId, command.ScopeId, requireActive: true, ct);
        ValidateTarget(command.TargetAmount, command.CycleKind);

        if (command.Amount < 0)
            throw new ValidationException("Set-aside amount must not be negative.");

        if (command.Amount > 0)
        {
            var available = await _balances.GetAvailableAsync(account.Id, ct);
            if (available < command.Amount)
                throw new ValidationException(
                    $"Insufficient available balance. Available: {available:N0}.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var setAside = new SetAside
        {
            ScopeId = command.ScopeId,
            AccountId = account.Id,
            Name = name,
            Kind = command.Kind,
            Note = NormalizeNote(command.Note),
            TargetAmount = command.TargetAmount,
            CycleKind = command.CycleKind,
            CycleAnchorDate = today,
            Status = SetAsideStatus.Active
        };

        _db.SetAsides.Add(setAside);

        if (command.Amount > 0)
        {
            _db.SetAsideEntries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = command.ScopeId,
                Type = SetAsideEntryType.Opened,
                Amount = command.Amount,
                Note = "Initial set-aside"
            });
        }

        await _db.SaveChangesAsync(ct);
        return setAside;
    }

    public Task<SetAside> UpdateSetAsideAsync(Guid setAsideId, UpdateSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => UpdateInternalAsync(setAsideId, command, token), ct);

    private async Task<SetAside> UpdateInternalAsync(Guid setAsideId, UpdateSetAsideCommand command, CancellationToken ct)
    {
        var setAside = await RequireSetAsideAsync(setAsideId, command.ScopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Closed set-aside cannot be updated.");

        // Cycle yang sudah berganti dinormalisasi dulu dengan konfigurasi LAMA,
        // sebelum perubahan target/cycle dari command ini diterapkan.
        await NormalizeCycleAsync(setAside, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        if (command.Name is not null)
            setAside.Name = NormalizeName(command.Name);

        if (command.Note is not null)
            setAside.Note = NormalizeNote(command.Note);

        if (command.RemoveTarget)
        {
            setAside.TargetAmount = null;
            // Tanpa target, normalisasi cycle tidak bermakna.
            if (command.CycleKind is null or SetAsideCycleKind.None)
                setAside.CycleKind = SetAsideCycleKind.None;
        }
        else if (command.TargetAmount.HasValue)
        {
            ValidateTarget(command.TargetAmount, command.CycleKind ?? setAside.CycleKind);
            setAside.TargetAmount = command.TargetAmount;
        }

        if (command.CycleKind.HasValue && command.CycleKind.Value != setAside.CycleKind)
        {
            var mergedTarget = command.RemoveTarget ? null : command.TargetAmount ?? setAside.TargetAmount;
            ValidateTarget(mergedTarget, command.CycleKind.Value);
            setAside.CycleKind = command.CycleKind.Value;
            // Cycle baru dimulai hari ini agar window-nya deterministik.
            setAside.CycleAnchorDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (command.AccountId.HasValue && command.AccountId.Value != setAside.AccountId)
        {
            var targetAccount = await RequireAccountAsync(command.AccountId.Value, command.ScopeId, requireActive: true, ct);
            var reserved = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);
            var targetAvailable = await _balances.GetAvailableAsync(targetAccount.Id, ct);
            if (targetAvailable < reserved)
                throw new ValidationException(
                    $"Insufficient available balance in '{targetAccount.Name}' to move this set-aside. Available: {targetAvailable:N0}.");

            setAside.AccountId = targetAccount.Id;
        }

        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return setAside;
    }

    // ───────────────────────── Money operations ─────────────────────────

    /// <summary>Menambah saldo yang disisihkan (top-up). Mengurangi Uang Bebas, bukan Expense.</summary>
    public Task<SetAsideOperationResult> AddAsync(Guid setAsideId, AddToSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            if (command.Amount <= 0)
                throw new ValidationException("Add amount must be positive.");
            return await ApplyDeltaAsync(setAsideId, command.ScopeId, command.Amount,
                SetAsideEntryType.Added, command.Note, token);
        }, ct);

    /// <summary>Menarik kembali sebagian saldo disisihkan. Menambah Uang Bebas, bukan Expense.</summary>
    public Task<SetAsideOperationResult> WithdrawAsync(Guid setAsideId, WithdrawFromSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            if (command.Amount <= 0)
                throw new ValidationException("Withdrawal amount must be positive.");
            return await ApplyDeltaAsync(setAsideId, command.ScopeId, -command.Amount,
                SetAsideEntryType.Withdrawn, command.Note, token);
        }, ct);

    private async Task<SetAsideOperationResult> ApplyDeltaAsync(
        Guid setAsideId,
        Guid scopeId,
        decimal signedDelta,
        SetAsideEntryType entryType,
        string? note,
        CancellationToken ct)
    {
        var setAside = await RequireSetAsideAsync(setAsideId, scopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Closed set-aside cannot be changed.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var normalization = await NormalizeCycleAsync(setAside, today, ct);
        var current = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);

        if (signedDelta > 0)
        {
            var available = await _balances.GetAvailableAsync(setAside.AccountId, ct);
            if (available < signedDelta)
                throw new ValidationException($"Insufficient available balance. Available: {available:N0}.");
        }
        else
        {
            var withdraw = -signedDelta;
            if (current < withdraw)
                throw new ValidationException($"Insufficient set-aside balance. Reserved: {current:N0}.");
        }

        var entry = new SetAsideEntry
        {
            SetAsideId = setAside.Id,
            ScopeId = scopeId,
            Type = entryType,
            Amount = signedDelta,
            Note = NormalizeNote(note)
        };
        _db.SetAsideEntries.Add(entry);

        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync(setAside, entry, transactionId: null, normalization, ct);
    }

    /// <summary>
    /// Pengeluaran riil yang diambil dari set-aside.
    ///
    /// Membuat Transaction Expense (uang benar-benar keluar) DAN melepas
    /// min(amount, reserved) dari set-aside dalam satu operasi atomik.
    /// Kelebihan di atas saldo disisihkan ditanggung oleh Uang Bebas —
    /// dari akun SetAside, atau dari FreeCashAccountId bila dispesifikasi.
    /// Plafon/target bukan hard limit: pemakaian di atas saldo SetAside tetap sah
    /// selama available balance sumber mencukupi.
    /// </summary>
    public Task<SetAsideOperationResult> SpendAsync(Guid setAsideId, SpendFromSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => SpendInternalAsync(setAsideId, command, token), ct);

    private async Task<SetAsideOperationResult> SpendInternalAsync(
        Guid setAsideId, SpendFromSetAsideCommand command, CancellationToken ct)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Spend amount must be positive.");

        var setAside = await RequireSetAsideAsync(setAsideId, command.ScopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Closed set-aside cannot be spent.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var normalization = await NormalizeCycleAsync(setAside, today, ct);

        var current = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);
        var fromSetAside = Math.Min(command.Amount, current);
        var shortfall = command.Amount - fromSetAside;

        var posAccount = await RequireAccountAsync(setAside.AccountId, command.ScopeId, requireActive: true, ct);

        Guid freeCashAccountId = command.FreeCashAccountId ?? setAside.AccountId;
        Account freeCashAccount;
        if (freeCashAccountId == posAccount.Id)
        {
            freeCashAccount = posAccount;
        }
        else
        {
            freeCashAccount = await RequireAccountAsync(freeCashAccountId, command.ScopeId, requireActive: true, ct);
        }

        // Porsi Uang Bebas tidak boleh melebihi available/free balance rekening sumber.
        // Available sudah nets out semua active set-asides di akun tersebut.
        if (shortfall > 0)
        {
            var freeCashAvailable = await _balances.GetAvailableAsync(freeCashAccount.Id, ct);
            if (freeCashAvailable < shortfall)
                throw new ValidationException(
                    $"Insufficient funds for free-cash portion. Available: {freeCashAvailable:N0}, needed: {shortfall:N0}.");
        }

        var transaction = new Transaction
        {
            ScopeId = command.ScopeId,
            Type = TransactionType.Expense,
            Amount = command.Amount,
            Description = command.Description,
            CategoryName = command.CategoryName,
            OccurredOn = command.OccurredOn
        };
        _db.Transactions.Add(transaction);

        // Expense satu kali dengan nominal penuh.
        // Porsi SetAside keluar dari akun pos; porsi shortfall keluar dari rekening Uang Bebas.
        if (shortfall > 0 && freeCashAccount.Id == posAccount.Id)
        {
            _db.TransactionEntries.Add(new TransactionEntry
            {
                TransactionId = transaction.Id,
                AccountId = posAccount.Id,
                Amount = -command.Amount
            });
        }
        else
        {
            if (fromSetAside > 0)
            {
                _db.TransactionEntries.Add(new TransactionEntry
                {
                    TransactionId = transaction.Id,
                    AccountId = posAccount.Id,
                    Amount = -fromSetAside
                });
            }
            if (shortfall > 0)
            {
                _db.TransactionEntries.Add(new TransactionEntry
                {
                    TransactionId = transaction.Id,
                    AccountId = freeCashAccount.Id,
                    Amount = -shortfall
                });
            }
        }

        var entry = new SetAsideEntry
        {
            SetAsideId = setAside.Id,
            ScopeId = command.ScopeId,
            Type = SetAsideEntryType.Spent,
            Amount = -fromSetAside,
            TransactionId = transaction.Id,
            Note = NormalizeNote(command.Note)
        };
        _db.SetAsideEntries.Add(entry);

        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync(setAside, entry, transaction.Id, normalization, ct);
    }

    /// <summary>
    /// Menutup set-aside dan melepas sisa saldo yang masih disisihkan ke Uang Bebas.
    /// Menutup tidak pernah membuat Transaction.
    /// </summary>
    public Task<SetAsideOperationResult> CloseAsync(Guid setAsideId, CloseSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => CloseInternalAsync(setAsideId, command, token), ct);

    private async Task<SetAsideOperationResult> CloseInternalAsync(
        Guid setAsideId, CloseSetAsideCommand command, CancellationToken ct)
    {
        var setAside = await RequireSetAsideAsync(setAsideId, command.ScopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Set-aside is already closed.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var normalization = await NormalizeCycleAsync(setAside, today, ct);

        var remaining = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);

        var entry = new SetAsideEntry
        {
            SetAsideId = setAside.Id,
            ScopeId = command.ScopeId,
            Type = SetAsideEntryType.Closed,
            Amount = -remaining,
            Note = NormalizeNote(command.Note) ?? $"Closed: {command.Reason}"
        };
        _db.SetAsideEntries.Add(entry);

        setAside.Status = SetAsideStatus.Closed;
        setAside.CloseReason = command.Reason;
        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync(setAside, entry, transactionId: null, normalization, ct);
    }

    // ───────────────────────── Cycle normalization ─────────────────────────

    /// <summary>
    /// Target saldo per cycle: pada awal cycle baru saldo dinormalisasi ke TargetAmount.
    ///
    ///   fundingRequired = max(0, TargetAmount - current)
    ///   surplus         = max(0, current - TargetAmount)
    ///
    /// Funding berasal dari Uang Bebas dan tercatat di history set-aside — BUKAN Expense,
    /// karena expense-nya sudah tercatat saat transaksinya benar-benar terjadi.
    /// Surplus tidak hilang: dikembalikan ke Uang Bebas.
    ///
    /// Bila Uang Bebas tidak cukup, hanya bagian yang mampu didanai yang ditambahkan.
    /// Kekurangannya direpresentasikan eksplisit sebagai shortfall — saldo fiktif tidak
    /// pernah diciptakan dan balance tidak pernah dibuat negatif untuk memenuhi target.
    ///
    /// Hanya dipanggil dari jalur write. GET tidak pernah mengubah database.
    /// </summary>
    private async Task<CycleNormalization> NormalizeCycleAsync(
        SetAside setAside, DateOnly today, CancellationToken ct)
    {
        var result = new CycleNormalization();
        if (!SetAsideCycle.IsRolloverPending(setAside, today))
            return result;

        if (setAside.TargetAmount.HasValue)
        {
            var current = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);
            var target = setAside.TargetAmount.Value;

            if (current > target)
            {
                var surplus = current - target;
                _db.SetAsideEntries.Add(new SetAsideEntry
                {
                    SetAsideId = setAside.Id,
                    ScopeId = setAside.ScopeId,
                    Type = SetAsideEntryType.Released,
                    Amount = -surplus,
                    Note = "Cycle surplus returned to available money"
                });
                result.Surplus = surplus;
            }
            else if (current < target)
            {
                var required = target - current;
                var available = await _balances.GetAvailableAsync(setAside.AccountId, ct);
                var applied = Math.Min(required, Math.Max(0m, available));

                if (applied > 0)
                {
                    _db.SetAsideEntries.Add(new SetAsideEntry
                    {
                        SetAsideId = setAside.Id,
                        ScopeId = setAside.ScopeId,
                        Type = SetAsideEntryType.CycleFunding,
                        Amount = applied,
                        Note = "Cycle funding from available money"
                    });
                }

                result.FundingRequired = required;
                result.FundingApplied = applied;
                result.Shortfall = required - applied;
            }
        }

        setAside.CycleAnchorDate = SetAsideCycle.CurrentCycleStart(
            setAside.CycleAnchorDate, setAside.CycleKind, today);
        setAside.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return result;
    }

    // ───────────────────────── Queries ─────────────────────────

    public Task<List<SetAsideProjection>> GetSetAsidesAsync(Guid scopeId, CancellationToken ct = default)
        => ProjectForScopeAsync(scopeId, filter: null, includeRecentEntries: false, ct);

    public async Task<SetAsideProjection?> GetSetAsideAsync(Guid setAsideId, Guid scopeId, CancellationToken ct = default)
    {
        var exists = await _db.SetAsides.AnyAsync(sa => sa.Id == setAsideId && sa.ScopeId == scopeId, ct);
        if (!exists) return null;

        var result = await ProjectForScopeAsync(scopeId, sa => sa.Id == setAsideId, includeRecentEntries: true, ct);
        return result.SingleOrDefault();
    }

    public async Task<List<SetAsideEntryProjection>> GetHistoryAsync(
        Guid setAsideId, Guid scopeId, CancellationToken ct = default)
    {
        var exists = await _db.SetAsides.AnyAsync(sa => sa.Id == setAsideId && sa.ScopeId == scopeId, ct);
        if (!exists)
            throw new ValidationException("Set-aside not found in current Scope.");

        return await _db.SetAsideEntries
            .Where(se => se.SetAsideId == setAsideId)
            .OrderByDescending(se => se.CreatedAt)
            .Select(se => new SetAsideEntryProjection
            {
                Id = se.Id,
                Type = se.Type,
                Amount = se.Amount,
                TransactionId = se.TransactionId,
                Note = se.Note,
                CreatedAt = se.CreatedAt
            })
            .ToListAsync(ct);
    }

    // ───────────────────────── Projection ─────────────────────────

    /// <summary>
    /// Builds projections for a scope. Cycle-funding shortfall is resolved against the
    /// whole scope so that several set-asides sharing one account cannot each claim the
    /// same available money.
    /// </summary>
    internal async Task<List<SetAsideProjection>> ProjectForScopeAsync(
        Guid scopeId,
        Expression<Func<SetAside, bool>>? filter,
        bool includeRecentEntries,
        CancellationToken ct)
    {
        var scopeSetAsides = await _db.SetAsides.Where(sa => sa.ScopeId == scopeId).ToListAsync(ct);
        if (scopeSetAsides.Count == 0) return [];

        var selected = filter is null
            ? scopeSetAsides
            : scopeSetAsides.AsQueryable().Where(filter).ToList();

        if (selected.Count == 0) return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var allIds = scopeSetAsides.Select(sa => sa.Id).ToList();
        var accountIds = scopeSetAsides.Select(sa => sa.AccountId).Distinct().ToList();

        var amounts = await _balances.GetSetAsideAmountsAsync(allIds, ct);
        var actuals = await _balances.GetActualBalancesAsync(accountIds, ct);
        var reservedByAccount = await _balances.GetActiveSetAsidesAsync(accountIds, ct);

        var accountNames = await _db.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        // "Terpakai" dihitung dari history Spend pada cycle berjalan,
        // atau sepanjang waktu bila set-aside tidak memiliki cycle.
        //
        // Entry Spent hanya menyimpan porsi yang keluar dari saldo pos
        // (min(amount, reserved)); porsi Uang Bebas tidak masuk ke sana.
        // Supaya pemakaian yang melewati plafon tetap terlihat, nominalnya
        // diambil dari TransactionEntries transaksi yang terkait.
        var spentEntries = await _db.SetAsideEntries
            .Where(se => allIds.Contains(se.SetAsideId) && se.Type == SetAsideEntryType.Spent)
            .Select(se => new { se.SetAsideId, se.Amount, se.CreatedAt, se.TransactionId })
            .ToListAsync(ct);

        var spentTransactionIds = spentEntries
            .Where(e => e.TransactionId != null)
            .Select(e => e.TransactionId!.Value)
            .Distinct()
            .ToList();

        var spendByTransaction = spentTransactionIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.TransactionEntries
                .Where(te => spentTransactionIds.Contains(te.TransactionId))
                .GroupBy(te => te.TransactionId)
                .Select(g => new { g.Key, Spent = -g.Sum(te => te.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Spent, ct);

        var usedBySetAside = new Dictionary<Guid, decimal>();
        foreach (var sa in scopeSetAsides)
        {
            var (from, to) = SetAsideCycle.UsageWindow(sa, today);
            var used = spentEntries
                .Where(e => e.SetAsideId == sa.Id)
                .Where(e => from is null || e.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue))
                .Where(e => to is null || e.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue))
                .Sum(e => e.TransactionId != null
                    && spendByTransaction.TryGetValue(e.TransactionId.Value, out var spend)
                        ? spend
                        : -e.Amount);
            usedBySetAside[sa.Id] = used;
        }

        // Pending cycle funding is allocated across set-asides of the same account in
        // creation order so the reported shortfall matches what normalization will do.
        var pendingFunding = new Dictionary<Guid, decimal>();
        var pendingSurplus = new Dictionary<Guid, decimal>();
        var pendingShortfall = new Dictionary<Guid, decimal>();
        var availableByAccount = accountIds.ToDictionary(
            id => id,
            id => actuals.GetValueOrDefault(id, 0m) - reservedByAccount.GetValueOrDefault(id, 0m));

        foreach (var accountGroup in scopeSetAsides
                     .Where(sa => sa.Status == SetAsideStatus.Active)
                     .GroupBy(sa => sa.AccountId))
        {
            var available = Math.Max(0m, availableByAccount.GetValueOrDefault(accountGroup.Key, 0m));

            foreach (var sa in accountGroup.OrderBy(sa => sa.CreatedAt))
            {
                if (!SetAsideCycle.IsRolloverPending(sa, today) || !sa.TargetAmount.HasValue)
                {
                    pendingFunding[sa.Id] = 0m;
                    pendingSurplus[sa.Id] = 0m;
                    pendingShortfall[sa.Id] = 0m;
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
                    available += surplus;
                }
                else if (current < target)
                {
                    var required = target - current;
                    var applied = Math.Min(required, available);
                    pendingFunding[sa.Id] = required;
                    pendingSurplus[sa.Id] = 0m;
                    pendingShortfall[sa.Id] = required - applied;
                    available -= applied;
                }
                else
                {
                    pendingFunding[sa.Id] = 0m;
                    pendingSurplus[sa.Id] = 0m;
                    pendingShortfall[sa.Id] = 0m;
                }
            }
        }

        Dictionary<Guid, List<SetAsideEntryProjection>>? recentBySetAside = null;
        if (includeRecentEntries)
        {
            var selectedIds = selected.Select(sa => sa.Id).ToList();
            var entries = await _db.SetAsideEntries
                .Where(se => selectedIds.Contains(se.SetAsideId))
                .OrderByDescending(se => se.CreatedAt)
                .ToListAsync(ct);

            recentBySetAside = entries
                .GroupBy(se => se.SetAsideId)
                .ToDictionary(g => g.Key, g => g.Take(20).Select(ProjectEntry).ToList());
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
                    AccountName = accountNames.GetValueOrDefault(sa.AccountId, ""),
                    Name = sa.Name,
                    Kind = sa.Kind,
                    Note = sa.Note,
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
                    Status = sa.Status,
                    CloseReason = sa.CloseReason,
                    CreatedAt = sa.CreatedAt,
                    UpdatedAt = sa.UpdatedAt,
                    RecentEntries = recentBySetAside?.GetValueOrDefault(sa.Id, []) ?? []
                };
            })
            .ToList();
    }

    private async Task<SetAsideOperationResult> BuildResultAsync(
        SetAside setAside,
        SetAsideEntry entry,
        Guid? transactionId,
        CycleNormalization normalization,
        CancellationToken ct)
    {
        var projected = await ProjectForScopeAsync(setAside.ScopeId, sa => sa.Id == setAside.Id,
            includeRecentEntries: false, ct);

        return new SetAsideOperationResult
        {
            SetAside = projected.Single(),
            Entry = ProjectEntry(entry),
            TransactionId = transactionId,
            CycleFundingShortfall = normalization.Shortfall
        };
    }

    private static SetAsideEntryProjection ProjectEntry(SetAsideEntry entry) => new()
    {
        Id = entry.Id,
        Type = entry.Type,
        Amount = entry.Amount,
        TransactionId = entry.TransactionId,
        Note = entry.Note,
        CreatedAt = entry.CreatedAt
    };

    private async Task<SetAside> RequireSetAsideAsync(Guid setAsideId, Guid scopeId, CancellationToken ct)
        => await _db.SetAsides
               .FirstOrDefaultAsync(sa => sa.Id == setAsideId && sa.ScopeId == scopeId, ct)
           ?? throw new ValidationException("Set-aside not found in current Scope.");

    private async Task<Account> RequireAccountAsync(Guid accountId, Guid scopeId, bool requireActive, CancellationToken ct)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.ScopeId == scopeId, ct)
            ?? throw new ValidationException("Account not found in current Scope.");

        if (requireActive && account.IsArchived)
            throw new ValidationException($"Cannot use archived Account '{account.Name}'.");

        return account;
    }

    private static void ValidateTarget(decimal? targetAmount, SetAsideCycleKind cycleKind)
    {
        if (targetAmount.HasValue && targetAmount.Value < 0)
            throw new ValidationException("Target amount must not be negative.");

        // Target dan cycle adalah dua konsep berbeda.
        // TargetAmount != null tidak berarti set-aside memiliki cycle.
        if (SetAsideCycle.HasCycle(cycleKind) && !targetAmount.HasValue)
            throw new ValidationException("A cycling set-aside requires a target amount.");
    }

    private static string NormalizeName(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ValidationException("Set-aside name is required.");
        if (trimmed.Length > 256)
            throw new ValidationException("Set-aside name must not exceed 256 characters.");
        return trimmed;
    }

    private static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > 512 ? trimmed[..512] : trimmed;
    }

    private sealed class CycleNormalization
    {
        public decimal FundingRequired { get; set; }
        public decimal FundingApplied { get; set; }
        public decimal Shortfall { get; set; }
        public decimal Surplus { get; set; }
    }
}
