using System.Linq.Expressions;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Uang yang Disisihkan (set-aside / pos alokasi).
///
/// SEBUAH POOL ALOKASI SCOPE-WIDE, independen dari Sumber Dana:
///   - tidak pernah membuat TransactionEntry atas nama akun
///   - tidak pernah mengubah saldo aktual akun manapun
///   - hanya mengurangi TotalAvailable / FreeCash (derived)
///
/// Sumber Dana (Account) hanya ditentukan pada saat transaksi/perpindahan uang:
///   - Create/Add: DefaultSourceAccountId hanya petunjuk rekening, bukan batas saldo
///   - Spend:     SourceAccountId wajib — seluruh nominal keluar dari akun itu
///
/// LEGACY SetAside.AccountId tidak pernah dipakai di method ini untuk perhitungan
/// saldo, uang yang bisa dipakai, pendanaan cycle, maupun alokasi.
///
/// Operasi uang riil hanya <see cref="SpendAsync"/> (Expense) — dibuat atomik
/// bersama pelepasan alokasi. Operasi lain (top-up/withdraw/close/cycle) hanya
/// mengubah ledger alokasi, bukan transaksi.
///
/// Setiap mutation berjalan dalam SERIALIZABLE transaction agar concurrent request
/// tidak bisa over-reserve, double-withdraw, atau spend terhadap stale balance.
///
/// Saldo pos diturunkan dari history:
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
        if (!Enum.IsDefined(command.Kind))
            throw new ValidationException("Jenis dana tidak valid.");
        if (!Enum.IsDefined(command.CycleKind))
            throw new ValidationException("Jenis siklus tidak valid.");
        ValidateTarget(command.TargetAmount, command.CycleKind);

        if (command.Kind == SetAsideKind.RoutineBatch && !SetAsideCycle.HasCycle(command.CycleKind))
            throw new ValidationException("Rutinitas berkala harus memiliki periode berulang.");

        if (command.Amount < 0)
            throw new ValidationException("Set-aside amount must not be negative.");

        if (command.SourceAccountId.HasValue)
        {
            // Rekening default hanya referensi; alokasi tidak mendebit rekening ini.
            await RequireAccountAsync(command.SourceAccountId.Value, command.ScopeId, requireActive: true, ct);
        }

        if (command.Amount > 0)
        {
            // Alokasi harus didukung Uang Bebas scope-wide; rekening referensi tidak didebit.
            var available = await _balances.GetScopeFreeCashAsync(command.ScopeId, ct);
            if (available < command.Amount)
                throw new ValidationException($"Uang Bebas tidak cukup. Tersedia: {available:N0}.");
        }

        var today = BusinessDate.TodayWib;
        var setAside = new SetAside
        {
            ScopeId = command.ScopeId,
            // AccountId tidak ditulis — alokasi independen dari Sumber Dana.
            AccountId = null,
            // Hint non-binding untuk pre-select UI pada proses manual.
            DefaultSourceAccountId = command.SourceAccountId,
            Name = name,
            Kind = command.Kind,
            Note = NormalizeNote(command.Note),
            TargetAmount = command.TargetAmount,
            CycleKind = command.CycleKind,
            CycleAnchorDate = SetAsideCycle.HasCycle(command.CycleKind)
                ? SetAsideCycle.CurrentCycleStart(today, command.CycleKind, today)
                : today,
            CycleFundingShortfall = SetAsideCycle.HasCycle(command.CycleKind) && command.TargetAmount.HasValue
                ? Math.Max(0m, command.TargetAmount.Value - command.Amount)
                : 0m,
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
        if (command.CycleKind.HasValue && !Enum.IsDefined(command.CycleKind.Value))
            throw new ValidationException("Jenis siklus tidak valid.");
        var setAside = await RequireSetAsideAsync(setAsideId, command.ScopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Closed set-aside cannot be updated.");

        var originalTarget = setAside.TargetAmount;
        var originalCycleKind = setAside.CycleKind;

        // Cycle yang sudah berganti dinormalisasi dulu dengan konfigurasi LAMA,
        // sebelum perubahan target/cycle dari command ini diterapkan.
        await NormalizeCycleAsync(setAside, BusinessDate.TodayWib, ct);

        if (command.Name is not null)
            setAside.Name = NormalizeName(command.Name);

        if (command.Note is not null)
            setAside.Note = NormalizeNote(command.Note);

        if (command.RemoveTarget)
        {
            setAside.TargetAmount = null;
            setAside.CycleFundingShortfall = 0m;
            // Tanpa target, normalisasi cycle tidak bermakna.
            if (command.CycleKind is null or SetAsideCycleKind.None)
                setAside.CycleKind = SetAsideCycleKind.None;
        }

        if (command.TargetAmount.HasValue)
        {
            ValidateTarget(command.TargetAmount, command.CycleKind ?? setAside.CycleKind);
            setAside.TargetAmount = command.TargetAmount;
        }

        if (command.CycleKind.HasValue && command.CycleKind.Value != setAside.CycleKind)
        {
            var mergedTarget = command.RemoveTarget ? null : command.TargetAmount ?? setAside.TargetAmount;
            ValidateTarget(mergedTarget, command.CycleKind.Value);
            if (setAside.Kind == SetAsideKind.RoutineBatch
                && !SetAsideCycle.HasCycle(command.CycleKind.Value))
                throw new ValidationException("Rutinitas berkala harus memiliki periode berulang.");
            setAside.CycleKind = command.CycleKind.Value;
            // Cycle baru dimulai hari ini agar window-nya deterministik.
            var today = BusinessDate.TodayWib;
            setAside.CycleAnchorDate = SetAsideCycle.CurrentCycleStart(today, setAside.CycleKind, today);
        }

        if (command.RemoveTarget
            || command.TargetAmount.HasValue && command.TargetAmount.Value != originalTarget
            || command.CycleKind.HasValue && command.CycleKind.Value != originalCycleKind)
        {
            var currentAmount = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);
            setAside.CycleFundingShortfall = SetAsideCycle.HasCycle(setAside.CycleKind)
                && setAside.TargetAmount.HasValue
                ? Math.Max(0m, setAside.TargetAmount.Value - currentAmount)
                : 0m;
        }

        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return setAside;
    }

    // ───────────────────────── Money operations ─────────────────────────

    /// <summary>Menambah alokasi dari Uang Bebas scope-wide, bukan Expense atau debit rekening.</summary>
    public Task<SetAsideOperationResult> AddAsync(Guid setAsideId, AddToSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            if (command.Amount <= 0)
                throw new ValidationException("Add amount must be positive.");
            return await ApplyDeltaAsync(setAsideId, command.ScopeId, command.Amount,
                SetAsideEntryType.Added, command.Note, command.SourceAccountId, token);
        }, ct);

    /// <summary>Melepas alokasi kembali ke Uang Bebas, bukan pemasukan atau kredit rekening.</summary>
    public Task<SetAsideOperationResult> WithdrawAsync(Guid setAsideId, WithdrawFromSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            if (command.Amount <= 0)
                throw new ValidationException("Withdrawal amount must be positive.");
            return await ApplyDeltaAsync(setAsideId, command.ScopeId, -command.Amount,
                SetAsideEntryType.Withdrawn, command.Note, sourceAccountId: null, token);
        }, ct);

    private async Task<SetAsideOperationResult> ApplyDeltaAsync(
        Guid setAsideId,
        Guid scopeId,
        decimal signedDelta,
        SetAsideEntryType entryType,
        string? note,
        Guid? sourceAccountId,
        CancellationToken ct)
    {
        var setAside = await RequireSetAsideAsync(setAsideId, scopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Closed set-aside cannot be changed.");

        var today = BusinessDate.TodayWib;
        var normalization = await NormalizeCycleAsync(setAside, today, ct);
        var current = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);

        if (signedDelta > 0)
        {
            // Rekening pilihan hanya disimpan sebagai preferensi; tidak membatasi saldo.
            if (sourceAccountId.HasValue)
            {
                await RequireAccountAsync(sourceAccountId.Value, scopeId, requireActive: true, ct);
                setAside.DefaultSourceAccountId = sourceAccountId;
            }

            // Alokasi tambahan hanya memakai Uang Bebas scope-wide.
            var available = await _balances.GetScopeFreeCashAsync(scopeId, ct);
            if (available < signedDelta)
                throw new ValidationException($"Uang Bebas tidak cukup. Tersedia: {available:N0}.");
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

        if (signedDelta > 0 && setAside.CycleFundingShortfall > 0m)
            setAside.CycleFundingShortfall = Math.Max(0m, setAside.CycleFundingShortfall - signedDelta);

        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync(setAside, entry, transactionId: null, normalization, ct);
    }

    /// <summary>
    /// Pengeluaran riil yang diambil dari set-aside.
    ///
    /// Membuat Transaction Expense (uang benar-benar keluar dari SourceAccountId)
    /// DAN melepas min(amount, saldoPos) dari set-aside dalam satu operasi atomik.
    ///
    /// SourceAccountId = Sumber Dana tempat uang keluar. Wajib. Seluruh nominal
    /// keluar dari akun itu (validasi saldo aktual). Porsi di atas saldo pos
    /// (shortfall) ditanggung Uang Bebas scope-wide.
    /// Pos tidak terikat ke akun manapun.
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

        var today = BusinessDate.TodayWib;
        var normalization = await NormalizeCycleAsync(setAside, today, ct);

        var current = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);
        if (setAside.Kind == SetAsideKind.RoutineBatch
            && await HasExecutionInCurrentCycleAsync(setAside, today, ct))
        {
            throw new ValidationException("Rutinitas berkala ini sudah direalisasikan pada periode berjalan.");
        }

        var fromSetAside = Math.Min(command.Amount, current);
        var shortfall = command.Amount - fromSetAside;

        // Sumber Dana tempat uang benar-benar keluar.
        var sourceAccount = await RequireAccountAsync(command.SourceAccountId, command.ScopeId, requireActive: true, ct);
        var sourceBalance = await _balances.GetActualBalanceAsync(sourceAccount.Id, ct);
        if (sourceBalance < command.Amount)
            throw new ValidationException(
                $"Insufficient balance in '{sourceAccount.Name}'. Balance: {sourceBalance:N0}, needed: {command.Amount:N0}.");

        // Porsi di atas saldo pos harus didukung uang yang belum dialokasikan.
        if (shortfall > 0)
        {
            var available = await _balances.GetScopeFreeCashAsync(command.ScopeId, ct);
            if (available < shortfall)
                throw new ValidationException(
                    $"Insufficient available money for free portion. Available: {available:N0}, needed: {shortfall:N0}.");
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

        // Seluruh nominal keluar dari Sumber Dana yang dipilih.
        _db.TransactionEntries.Add(new TransactionEntry
        {
            TransactionId = transaction.Id,
            AccountId = sourceAccount.Id,
            Amount = -command.Amount
        });

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

        // Rutinitas berkala diselesaikan satu kali per periode. Sisa yang tidak
        // terpakai langsung dilepas agar menjadi uang belum dialokasikan.
        // Biaya aktual tetap boleh melebihi saldo pos (shortfall telah divalidasi).
        if (setAside.Kind == SetAsideKind.RoutineBatch)
        {
            var remaining = current - fromSetAside;
            if (remaining > 0)
            {
                _db.SetAsideEntries.Add(new SetAsideEntry
                {
                    SetAsideId = setAside.Id,
                    ScopeId = command.ScopeId,
                    Type = SetAsideEntryType.Released,
                    Amount = -remaining,
                    TransactionId = transaction.Id,
                    Note = "Unused cycle allocation returned to available money"
                });
            }
        }

        // Pos Sekali Pakai selesai ketika pengeluaran riil dicatat.
        // Tutup dan lepaskan sisa dalam transaksi yang sama dengan expense.
        if (setAside.Kind == SetAsideKind.SingleSpend)
        {
            var remaining = current - fromSetAside;
            _db.SetAsideEntries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = command.ScopeId,
                Type = SetAsideEntryType.Closed,
                Amount = -remaining,
                TransactionId = transaction.Id,
                Note = "Single-spend set-aside completed"
            });
            setAside.Status = SetAsideStatus.Closed;
            setAside.CloseReason = SetAsideCloseReason.Spent;
            setAside.CycleFundingShortfall = 0m;
        }

        setAside.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync(setAside, entry, transaction.Id, normalization, ct);
    }

    /// <summary>
    /// Mencatat expense yang memakai pos dari jalur transaksi umum atau realisasi agenda.
    /// Menyatukan aturan cycle, batas satu eksekusi RoutineBatch, pelepasan sisa, dan
    /// penutupan SingleSpend agar semua entry point memiliki semantik yang sama.
    /// Pemanggil harus sudah membuka transaksi database.
    /// </summary>
    internal async Task<List<SetAsideEntry>> RecordTransactionExpenseAsync(
        Guid setAsideId,
        Guid scopeId,
        Transaction transaction,
        decimal amount,
        CancellationToken ct)
    {
        var setAside = await RequireSetAsideAsync(setAsideId, scopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Set-aside not found or not active in current Scope.");

        var today = BusinessDate.TodayWib;
        await NormalizeCycleAsync(setAside, today, ct);

        if (setAside.Kind == SetAsideKind.RoutineBatch
            && await HasExecutionInCurrentCycleAsync(setAside, today, ct))
        {
            throw new ValidationException("Rutinitas berkala ini sudah direalisasikan pada periode berjalan.");
        }

        var current = await _balances.GetSetAsideAmountAsync(setAside.Id, ct);
        var fromSetAside = Math.Min(amount, current);
        var shortfall = amount - fromSetAside;
        if (shortfall > 0)
        {
            var available = await _balances.GetScopeFreeCashAsync(scopeId, ct);
            if (available < shortfall)
                throw new ValidationException(
                    $"Insufficient available money for free portion. Available: {available:N0}, needed: {shortfall:N0}.");
        }

        var entries = new List<SetAsideEntry>
        {
            new()
            {
                SetAsideId = setAside.Id,
                ScopeId = scopeId,
                Type = SetAsideEntryType.Spent,
                Amount = -fromSetAside,
                TransactionId = transaction.Id,
                Note = "Allocation released by transaction"
            }
        };

        if (setAside.Kind == SetAsideKind.RoutineBatch)
        {
            var remaining = current - fromSetAside;
            if (remaining > 0)
            {
                entries.Add(new SetAsideEntry
                {
                    SetAsideId = setAside.Id,
                    ScopeId = scopeId,
                    Type = SetAsideEntryType.Released,
                    Amount = -remaining,
                    TransactionId = transaction.Id,
                    Note = "Unused cycle allocation returned to available money"
                });
            }
        }
        else if (setAside.Kind == SetAsideKind.SingleSpend)
        {
            var remaining = current - fromSetAside;
            entries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = scopeId,
                Type = SetAsideEntryType.Closed,
                Amount = -remaining,
                TransactionId = transaction.Id,
                Note = "Single-spend set-aside completed"
            });
            setAside.Status = SetAsideStatus.Closed;
            setAside.CloseReason = SetAsideCloseReason.Spent;
        }

        setAside.UpdatedAt = DateTime.UtcNow;
        return entries;
    }

    private async Task<bool> HasExecutionInCurrentCycleAsync(
        SetAside setAside, DateOnly today, CancellationToken ct)
    {
        var (from, to) = SetAsideCycle.UsageWindow(setAside, today);
        if (from is null || to is null) return false;

        var fromUtc = BusinessDate.StartOfDayUtc(from.Value);
        var toExclusiveUtc = BusinessDate.StartOfDayUtc(to.Value.AddDays(1));
        return await _db.SetAsideEntries.AnyAsync(se =>
            se.SetAsideId == setAside.Id
            && se.Type == SetAsideEntryType.Spent
            && se.TransactionId != null
            && se.CreatedAt >= fromUtc
            && se.CreatedAt < toExclusiveUtc
            && !_db.Transactions.Any(reversal =>
                reversal.ScopeId == setAside.ScopeId
                && reversal.Type == TransactionType.Reversal
                && reversal.RelatedTransactionId == se.TransactionId), ct);
    }

    /// <summary>
    /// Menutup set-aside dan melepas sisa saldo yang masih disisihkan ke TotalAvailable.
    /// Menutup tidak pernah membuat Transaction.
    /// </summary>
    public Task<SetAsideOperationResult> CloseAsync(Guid setAsideId, CloseSetAsideCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => CloseInternalAsync(setAsideId, command, token), ct);

    private async Task<SetAsideOperationResult> CloseInternalAsync(
        Guid setAsideId, CloseSetAsideCommand command, CancellationToken ct)
    {
        if (!Enum.IsDefined(command.Reason))
            throw new ValidationException("Alasan penutupan tidak valid.");
        var setAside = await RequireSetAsideAsync(setAsideId, command.ScopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Set-aside is already closed.");

        var today = BusinessDate.TodayWib;
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
        setAside.CycleFundingShortfall = 0m;
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
    /// Funding berasal dari Uang Bebas scope-wide (uang yang belum dialokasikan
    /// atau dijanjikan ke agenda pengeluaran)
    /// dan tercatat di history set-aside — BUKAN Expense, karena expense-nya sudah
    /// tercatat saat transaksinya benar-benar terjadi. Surplus tidak hilang:
    /// dikembalikan ke TotalAvailable.
    ///
    /// Bila Uang Bebas tidak cukup, hanya bagian yang mampu didanai yang ditambahkan.
    /// Kekurangannya direpresentasikan eksplisit sebagai shortfall — saldo fiktif tidak
    /// pernah diciptakan dan balance tidak pernah dibuat negatif untuk memenuhi target.
    ///
    /// Hanya dipanggil dari jalur write. GET tidak pernah mengubah database.
    /// </summary>
    private async Task<CycleNormalization> NormalizeCycleAsync(
        SetAside setAside, DateOnly today, CancellationToken ct, bool persistChanges = true)
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
                setAside.CycleFundingShortfall = 0m;
            }
            else if (current < target)
            {
                var required = target - current;
                // Funding hanya dari Uang Bebas; kekurangan target tidak mengikat uang
                // yang belum tersedia dan akan dicoba kembali saat ada pemasukan.
                var available = await _balances.GetScopeFreeCashAsync(setAside.ScopeId, ct);
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
                setAside.CycleFundingShortfall = result.Shortfall;
            }
            else
            {
                setAside.CycleFundingShortfall = 0m;
            }
        }
        else
        {
            setAside.CycleFundingShortfall = 0m;
        }

        setAside.CycleAnchorDate = SetAsideCycle.CurrentCycleStart(
            setAside.CycleAnchorDate, setAside.CycleKind, today);
        setAside.UpdatedAt = DateTime.UtcNow;

        if (persistChanges)
            await _db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// On income, roll active cycles forward and automatically use available FreeCash
    /// to reduce remaining funding shortfalls in creation order.
    /// Caller owns the surrounding serializable transaction and has saved the income.
    /// </summary>
    internal async Task ApplyIncomeToCycleShortfallsAsync(
        Guid scopeId, Guid incomeTransactionId, DateOnly today, CancellationToken ct)
    {
        var active = await _db.SetAsides
            .Where(sa => sa.ScopeId == scopeId && sa.Status == SetAsideStatus.Active)
            .OrderBy(sa => sa.CreatedAt)
            .ToListAsync(ct);

        foreach (var setAside in active)
            await NormalizeCycleAsync(setAside, today, ct);

        var available = Math.Max(0m, await _balances.GetScopeFreeCashAsync(scopeId, ct));
        foreach (var setAside in active.Where(sa =>
                     SetAsideCycle.HasCycle(sa.CycleKind)
                     && sa.TargetAmount.HasValue
                     && sa.CycleFundingShortfall > 0m))
        {
            if (available <= 0m) break;

            var funded = Math.Min(setAside.CycleFundingShortfall, available);
            _db.SetAsideEntries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = scopeId,
                Type = SetAsideEntryType.CycleFunding,
                Amount = funded,
                TransactionId = incomeTransactionId,
                Note = "Cycle shortfall funded from new income"
            });
            setAside.CycleFundingShortfall -= funded;
            setAside.UpdatedAt = DateTime.UtcNow;
            available -= funded;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Explicitly allocated income funds the selected set-aside's current-cycle
    /// shortfall first. Splitting the ledger entry lets reversal restore that
    /// shortfall contribution precisely.
    /// </summary>
    internal async Task<List<SetAsideEntry>> RecordTransactionIncomeAsync(
        Guid setAsideId, Guid scopeId, Transaction transaction, decimal amount, CancellationToken ct)
    {
        var setAside = await RequireSetAsideAsync(setAsideId, scopeId, ct);
        if (setAside.Status != SetAsideStatus.Active)
            throw new ValidationException("Set-aside not found or not active in current Scope.");

        await NormalizeCycleAsync(setAside, BusinessDate.TodayWib, ct, persistChanges: false);

        var cycleFunding = SetAsideCycle.HasCycle(setAside.CycleKind)
            ? Math.Min(amount, setAside.CycleFundingShortfall)
            : 0m;
        setAside.CycleFundingShortfall -= cycleFunding;
        setAside.UpdatedAt = DateTime.UtcNow;

        var entries = new List<SetAsideEntry>();
        if (cycleFunding > 0m)
        {
            entries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = scopeId,
                Type = SetAsideEntryType.CycleFunding,
                Amount = cycleFunding,
                TransactionId = transaction.Id,
                Note = "Cycle shortfall funded by allocated income"
            });
        }

        var additionalAllocation = amount - cycleFunding;
        if (additionalAllocation > 0m)
        {
            entries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = scopeId,
                Type = SetAsideEntryType.Added,
                Amount = additionalAllocation,
                TransactionId = transaction.Id,
                Note = "Allocation funded by transaction"
            });
        }

        return entries;
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

    public async Task<SetAsideHistoryPage> GetHistoryAsync(
        Guid setAsideId, Guid scopeId, string? cursor = null, int pageSize = 30, CancellationToken ct = default)
    {
        var exists = await _db.SetAsides.AnyAsync(sa => sa.Id == setAsideId && sa.ScopeId == scopeId, ct);
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

        var query = _db.SetAsideEntries.Where(se => se.SetAsideId == setAsideId);
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
            : await _db.SetAsideEntries
                .Where(se => se.SetAsideId == setAsideId
                    && (se.CreatedAt > first.CreatedAt
                        || (se.CreatedAt == first.CreatedAt && se.Id.CompareTo(first.Id) > 0)))
                .SumAsync(se => (decimal?)se.Amount, ct) ?? 0m;
        var runningBalance = await _balances.GetSetAsideAmountAsync(setAsideId, ct) - newerBalanceDeltas;
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

    // ───────────────────────── Projection ─────────────────────────

    /// <summary>
    /// Builds projections for a scope. Pending cycle funding is resolved against the
    /// whole scope-wide available pool (TotalActual − TotalSetAside) so that several
    /// set-asides cannot each claim the same available money. Claim order = creation order.
    ///
    /// LEGACY SetAside.AccountId TIDAK dipakai di sini untuk perhitungan apa pun.
    /// Nilainya hanya diproyeksikan untuk kompatibilitas tampilan data lama.
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

        var today = BusinessDate.TodayWib;
        var allIds = scopeSetAsides.Select(sa => sa.Id).ToList();

        var amounts = await _balances.GetSetAsideAmountsAsync(allIds, ct);

        // Legacy account names — hanya untuk tampilan data lama, bukan perhitungan.
        var legacyAccountIds = scopeSetAsides
            .Where(sa => sa.AccountId.HasValue)
            .Select(sa => sa.AccountId!.Value)
            .Distinct()
            .ToList();
        // Default source names — hanya untuk pre-select/hint UI pada proses manual.
        var defaultSourceIds = scopeSetAsides
            .Where(sa => sa.DefaultSourceAccountId.HasValue)
            .Select(sa => sa.DefaultSourceAccountId!.Value)
            .Distinct()
            .ToList();
        var lookupIds = legacyAccountIds.Concat(defaultSourceIds).Distinct().ToList();
        var accountNames = lookupIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts
                .Where(a => lookupIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        // "Terpakai" dihitung dari history Spend pada cycle berjalan,
        // atau sepanjang waktu bila set-aside tidak memiliki cycle.
        //
        // Entry Spent hanya menyimpan porsi yang keluar dari saldo pos
        // (min(amount, reserved)); porsi uang bebas tidak masuk ke sana.
        // Supaya pemakaian yang melewati plafon tetap terlihat, nominalnya
        // diambil dari TransactionEntries transaksi yang terkait.
        var selectedIdsForUsage = selected.Select(sa => sa.Id).ToList();
        var spentEntries = await _db.SetAsideEntries
            .Where(se => selectedIdsForUsage.Contains(se.SetAsideId) && se.Type == SetAsideEntryType.Spent)
            .Select(se => new { se.SetAsideId, se.Amount, se.CreatedAt, se.TransactionId })
            .ToListAsync(ct);

        var spentTransactionIds = spentEntries
            .Where(e => e.TransactionId != null)
            .Select(e => e.TransactionId!.Value)
            .Distinct()
            .ToList();

        var reversedSpendTransactionIds = spentTransactionIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Transactions
                .Where(t => t.ScopeId == scopeId
                    && t.Type == TransactionType.Reversal
                    && t.RelatedTransactionId.HasValue
                    && spentTransactionIds.Contains(t.RelatedTransactionId.Value))
                .Select(t => t.RelatedTransactionId!.Value)
                .ToListAsync(ct))
                .ToHashSet();

        var spendByTransaction = spentTransactionIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.TransactionEntries
                .Where(te => spentTransactionIds.Contains(te.TransactionId))
                .GroupBy(te => te.TransactionId)
                .Select(g => new { g.Key, Spent = -g.Sum(te => te.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Spent, ct);

        var spendsBySetAside = spentEntries.GroupBy(e => e.SetAsideId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var usedBySetAside = new Dictionary<Guid, decimal>();
        foreach (var sa in selected)
        {
            var (from, to) = SetAsideCycle.UsageWindow(sa, today);
            var used = spendsBySetAside.GetValueOrDefault(sa.Id, [])
                .Where(e => from is null || e.CreatedAt >= BusinessDate.StartOfDayUtc(from.Value))
                .Where(e => to is null || e.CreatedAt < BusinessDate.StartOfDayUtc(to.Value.AddDays(1)))
                .Where(e => e.TransactionId is null
                    || !reversedSpendTransactionIds.Contains(e.TransactionId.Value))
                .Sum(e => e.TransactionId != null
                    && spendByTransaction.TryGetValue(e.TransactionId.Value, out var spend)
                        ? spend
                        : -e.Amount);
            usedBySetAside[sa.Id] = used;
        }

        var executedBySetAside = new Dictionary<Guid, bool>();
        foreach (var sa in selected)
        {
            var (from, to) = SetAsideCycle.UsageWindow(sa, today);
            executedBySetAside[sa.Id] = sa.Kind == SetAsideKind.RoutineBatch
                && from.HasValue
                && to.HasValue
                && spendsBySetAside.GetValueOrDefault(sa.Id, []).Any(e =>
                    e.TransactionId.HasValue
                    && !reversedSpendTransactionIds.Contains(e.TransactionId.Value)
                    && e.CreatedAt >= BusinessDate.StartOfDayUtc(from.Value)
                    && e.CreatedAt < BusinessDate.StartOfDayUtc(to.Value.AddDays(1)));
        }

        // Simulasikan pendanaan rollover dari Uang Bebas dengan urutan pos tertua.
        // Kekurangan yang belum didanai ditampilkan, tetapi tidak mengurangi Uang Bebas.
        var pendingFunding = new Dictionary<Guid, decimal>();
        var pendingSurplus = new Dictionary<Guid, decimal>();
        var pendingShortfall = new Dictionary<Guid, decimal>();

        var scopeAvailable = Math.Max(0m, await _balances.GetScopeFreeCashAsync(scopeId, ct));

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
            var entries = await _db.SetAsideEntries
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
                .ToDictionary(g => g.Key, g => g.ToList());
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
                    // Legacy only — bukan lokasi permanen alokasi.
                    AccountId = sa.AccountId,
                    AccountName = sa.AccountId.HasValue
                        ? accountNames.GetValueOrDefault(sa.AccountId.Value, "")
                        : null,
                    // Hint non-binding untuk pre-select UI proses manual.
                    DefaultSourceAccountId = sa.DefaultSourceAccountId,
                    DefaultSourceAccountName = sa.DefaultSourceAccountId.HasValue
                        ? accountNames.GetValueOrDefault(sa.DefaultSourceAccountId.Value, "")
                        : null,
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
            CycleFundingShortfall = projected.Single().CycleFundingShortfall
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
