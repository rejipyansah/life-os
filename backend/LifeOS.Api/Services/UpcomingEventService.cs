using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Rencana Pengeluaran/Pemasukan (upcoming cash events).
///
/// An expected event is an intention, NOT a Transaction:
///   - it never changes the actual account balance
///   - it never appears in real activity
///   - passing its date does not turn it into income or expense
///
/// Rencana TIDAK terikat ke Sumber Dana. Akun hanya dipilih saat
/// <see cref="RealizeAsync"/> — user menentukan Sumber Dana aktual saat
/// transaksi benar-benar dilakukan.
///
/// Only <see cref="RealizeAsync"/> turns an event into a real Transaction, and only when
/// the user says the event actually happened.
/// </summary>
public class UpcomingEventService
{
    private readonly ApplicationDbContext _db;
    private readonly BalanceCalculator _balances;
    private readonly SetAsideService _setAsides;

    public UpcomingEventService(ApplicationDbContext db, BalanceCalculator balances)
    {
        _db = db;
        _balances = balances;
        _setAsides = new SetAsideService(db, balances);
    }

    // ───────────────────────── Create / Update ─────────────────────────

    public Task<UpcomingEvent> CreateAsync(CreateUpcomingEventCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => CreateInternalAsync(command, token), ct);

    private async Task<UpcomingEvent> CreateInternalAsync(CreateUpcomingEventCommand command, CancellationToken ct)
    {
        var title = NormalizeRequired(command.Title, 256, "Agenda title is required.");

        if (command.Amount <= 0)
            throw new ValidationException("Agenda amount must be positive.");

        if (!Enum.IsDefined(command.Direction))
            throw new ValidationException($"Invalid direction: {command.Direction}");
        if (!Enum.IsDefined(command.ScheduleKind))
            throw new ValidationException($"Invalid schedule kind: {command.ScheduleKind}");
        if (!Enum.IsDefined(command.Recurrence))
            throw new ValidationException($"Invalid recurrence: {command.Recurrence}");

        ValidateSchedule(command.ScheduleKind, command.DueDate);

        var now = DateTime.UtcNow;
        var agenda = new UpcomingEvent
        {
            ScopeId = command.ScopeId,
            // AccountId tidak ditulis — Rencana tidak terikat Sumber Dana.
            // Akun hanya dipilih saat realizasi.
            AccountId = null,
            Title = title,
            Amount = command.Amount,
            Direction = command.Direction,
            CategoryName = NormalizeOptional(command.CategoryName, 128),
            Note = NormalizeOptional(command.Note, 512),
            DueDate = command.ScheduleKind == UpcomingEventScheduleKind.Scheduled ? command.DueDate : null,
            ScheduleKind = command.ScheduleKind,
            Recurrence = command.Recurrence,
            Status = UpcomingEventStatus.Scheduled,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.UpcomingEvents.Add(agenda);
        await _db.SaveChangesAsync(ct);
        return agenda;
    }

    public Task<UpcomingEvent> UpdateAsync(Guid eventId, UpdateUpcomingEventCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => UpdateInternalAsync(eventId, command, token), ct);

    private async Task<UpcomingEvent> UpdateInternalAsync(Guid eventId, UpdateUpcomingEventCommand command, CancellationToken ct)
    {
        var agenda = await RequireAsync(eventId, command.ScopeId, ct);
        if (agenda.Status != UpcomingEventStatus.Scheduled)
            throw new ValidationException("Only a scheduled agenda can be updated.");

        if (command.Title is not null)
            agenda.Title = NormalizeRequired(command.Title, 256, "Agenda title is required.");

        if (command.Amount.HasValue)
        {
            if (command.Amount.Value <= 0)
                throw new ValidationException("Agenda amount must be positive.");
            agenda.Amount = command.Amount.Value;
        }

        if (command.Direction.HasValue)
        {
            if (!Enum.IsDefined(command.Direction.Value))
                throw new ValidationException($"Invalid direction: {command.Direction.Value}");
            agenda.Direction = command.Direction.Value;
        }

        if (command.CategoryName is not null)
            agenda.CategoryName = NormalizeOptional(command.CategoryName, 128);

        if (command.Note is not null)
            agenda.Note = NormalizeOptional(command.Note, 512);

        if (command.ScheduleKind.HasValue)
        {
            if (!Enum.IsDefined(command.ScheduleKind.Value))
                throw new ValidationException($"Invalid schedule kind: {command.ScheduleKind.Value}");
            agenda.ScheduleKind = command.ScheduleKind.Value;
        }

        if (command.Recurrence.HasValue)
        {
            if (!Enum.IsDefined(command.Recurrence.Value))
                throw new ValidationException($"Invalid recurrence: {command.Recurrence.Value}");
            agenda.Recurrence = command.Recurrence.Value;
        }

        if (command.ClearDueDate)
        {
            agenda.DueDate = null;
        }
        else if (command.DueDate.HasValue)
        {
            agenda.DueDate = command.DueDate.Value;
        }

        ValidateSchedule(agenda.ScheduleKind, agenda.DueDate);
        if (agenda.ScheduleKind == UpcomingEventScheduleKind.Flexible)
            agenda.DueDate = null;

        agenda.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return agenda;
    }

    // ───────────────────────── Lifecycle ─────────────────────────

    /// <summary>
    /// Menunda agenda ke tanggal berikutnya tanpa mengubah data lainnya.
    /// Tanpa NewDueDate, tanggalnya digeser satu hari.
    /// </summary>
    public Task<UpcomingEvent> PostponeAsync(Guid eventId, PostponeUpcomingEventCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => PostponeInternalAsync(eventId, command, token), ct);

    private async Task<UpcomingEvent> PostponeInternalAsync(Guid eventId, PostponeUpcomingEventCommand command, CancellationToken ct)
    {
        var agenda = await RequireAsync(eventId, command.ScopeId, ct);
        if (agenda.Status != UpcomingEventStatus.Scheduled)
            throw new ValidationException("Only a scheduled agenda can be postponed.");

        if (agenda.ScheduleKind == UpcomingEventScheduleKind.Flexible && !command.NewDueDate.HasValue)
            throw new ValidationException("A flexible agenda has no date to postpone. Provide a new due date.");

        var newDate = command.NewDueDate
            ?? (agenda.DueDate ?? BusinessDate.TodayWib).AddDays(1);

        if (agenda.DueDate.HasValue && newDate < agenda.DueDate.Value)
            throw new ValidationException("Postponing must move the due date forward.");

        agenda.DueDate = newDate;
        agenda.ScheduleKind = UpcomingEventScheduleKind.Scheduled;
        agenda.StatusReason = NormalizeOptional(command.Reason, 512);
        agenda.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return agenda;
    }

    /// <summary>Menandai agenda dilewati. Tidak ada pergerakan uang.</summary>
    public Task<UpcomingEvent> SkipAsync(Guid eventId, SettleUpcomingEventCommand command, CancellationToken ct = default)
        => SettleAsync(eventId, command, UpcomingEventStatus.Skipped, ct);

    /// <summary>Membatalkan agenda. Tidak ada pergerakan uang.</summary>
    public Task<UpcomingEvent> CancelAsync(Guid eventId, SettleUpcomingEventCommand command, CancellationToken ct = default)
        => SettleAsync(eventId, command, UpcomingEventStatus.Cancelled, ct);

    private Task<UpcomingEvent> SettleAsync(
        Guid eventId, SettleUpcomingEventCommand command, UpcomingEventStatus status, CancellationToken ct)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            var agenda = await RequireAsync(eventId, command.ScopeId, token);
            if (agenda.Status != UpcomingEventStatus.Scheduled)
                throw new ValidationException("Only a scheduled agenda can be settled.");

            agenda.Status = status;
            agenda.StatusReason = NormalizeOptional(command.Reason, 512);
            agenda.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(token);
            return agenda;
        }, ct);

    /// <summary>
    /// Menghapus agenda dari daftar. Agenda yang sudah direalisasikan tidak dapat dihapus
    /// agar jejak audit tetap dapat ditelusuri dari transaksinya.
    /// </summary>
    public Task DeleteAsync(Guid eventId, Guid scopeId, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            var agenda = await RequireAsync(eventId, scopeId, token);
            if (agenda.Status == UpcomingEventStatus.Realized)
                throw new ValidationException("A realized agenda cannot be deleted; reverse its transaction instead.");

            _db.UpcomingEvents.Remove(agenda);
            await _db.SaveChangesAsync(token);
        }, ct);

    /// <summary>
    /// Merealisasikan agenda menjadi Transaction nyata.
    ///
    ///   Direction Income  → Transaction Income  (satu entry positif)
    ///   Direction Expense → Transaction Expense (satu entry negatif)
    ///
    /// AccountId pada command = Sumber Dana tempat uang benar-benar keluar/masuk.
    /// WAJIB dipilih saat realizasi — bukan terikat permanen dari saat rencana dibuat.
    /// SetAsideId = Dana yang Disisihkan opsional yang dialokasikan/dilepas.
    ///
    /// Sebelum ini dipanggil, agenda tidak pernah mengubah actual balance.
    /// </summary>
    public Task<RealizeUpcomingEventResult> RealizeAsync(Guid eventId, RealizeUpcomingEventCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => RealizeInternalAsync(eventId, command, token), ct);

    private async Task<RealizeUpcomingEventResult> RealizeInternalAsync(
        Guid eventId, RealizeUpcomingEventCommand command, CancellationToken ct)
    {
        var agenda = await RequireAsync(eventId, command.ScopeId, ct);
        if (agenda.Status != UpcomingEventStatus.Scheduled)
            throw new ValidationException("Only a scheduled agenda can be realized.");

        // Sumber Dana dipilih SAAT realizasi — bukan terikat permanen dari saat dibuat.
        if (!command.AccountId.HasValue)
            throw new ValidationException(
                "This realization must specify a source account (Sumber Dana). Select the actual account where money leaves/enters.");

        var account = await RequireAccountAsync(command.AccountId.Value, command.ScopeId, requireActive: true, ct);

        var occurredOn = command.OccurredOn ?? BusinessDate.TodayWib;
        var isExpense = agenda.Direction == UpcomingEventDirection.Expense;

        if (isExpense)
        {
            var balance = await _balances.GetActualBalanceAsync(account.Id, ct);
            if (balance < agenda.Amount)
                throw new ValidationException($"Insufficient balance in '{account.Name}'. Balance: {balance:N0}.");

            if (command.SetAsideId.HasValue)
            {
                var setAside = await _db.SetAsides
                    .FirstOrDefaultAsync(sa => sa.Id == command.SetAsideId.Value
                        && sa.ScopeId == command.ScopeId
                        && sa.Status == SetAsideStatus.Active, ct);

                if (setAside is null)
                    throw new ValidationException("Set-aside not found or not active in current Scope.");
            }
        }

        var transaction = new Transaction
        {
            ScopeId = command.ScopeId,
            Type = isExpense ? TransactionType.Expense : TransactionType.Income,
            Amount = agenda.Amount,
            Description = NormalizeOptional(command.Description, 512) ?? agenda.Title,
            CategoryName = agenda.CategoryName,
            OccurredOn = occurredOn,
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(transaction);

        _db.TransactionEntries.Add(new TransactionEntry
        {
            TransactionId = transaction.Id,
            AccountId = account.Id,
            Amount = isExpense ? -agenda.Amount : agenda.Amount
        });

        // Alokasi Dana yang Disisihkan — independen dari Sumber Dana.
        if (command.SetAsideId.HasValue)
        {
            if (isExpense)
            {
                var allocationEntries = await _setAsides.RecordTransactionExpenseAsync(
                    command.SetAsideId.Value, command.ScopeId, transaction, agenda.Amount, ct);
                _db.SetAsideEntries.AddRange(allocationEntries);
            }
            else
            {
                _db.SetAsideEntries.Add(new SetAsideEntry
                {
                    SetAsideId = command.SetAsideId.Value,
                    ScopeId = command.ScopeId,
                    Type = SetAsideEntryType.Added,
                    Amount = agenda.Amount,
                    TransactionId = transaction.Id,
                    Note = "Allocation funded by realized agenda"
                });
            }
        }

        agenda.Status = UpcomingEventStatus.Realized;
        agenda.RealizedTransactionId = transaction.Id;
        agenda.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new RealizeUpcomingEventResult
        {
            Event = Project(agenda, account.Name, BusinessDate.TodayWib),
            TransactionId = transaction.Id
        };
    }

    // ───────────────────────── Queries ─────────────────────────

    public async Task<List<UpcomingEventProjection>> GetAsync(Guid scopeId, CancellationToken ct = default)
    {
        var all = await _db.UpcomingEvents
            .Where(ue => ue.ScopeId == scopeId)
            .ToListAsync(ct);

        // Legacy account names — hanya untuk kompatibilitas data lama, bukan perhitungan.
        var accountIds = all.Where(ue => ue.AccountId.HasValue).Select(ue => ue.AccountId!.Value).Distinct().ToList();
        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var today = BusinessDate.TodayWib;
        return all
            .OrderBy(ue => ue.Status == UpcomingEventStatus.Scheduled ? 0 : 1)
            .ThenBy(ue => ue.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(ue => ue.CreatedAt)
            .Select(ue => Project(ue, ue.AccountId is null ? null : accountNames.GetValueOrDefault(ue.AccountId.Value), today))
            .ToList();
    }

    public async Task<UpcomingEventProjection?> GetByIdAsync(Guid eventId, Guid scopeId, CancellationToken ct = default)
    {
        var agenda = await _db.UpcomingEvents
            .FirstOrDefaultAsync(ue => ue.Id == eventId && ue.ScopeId == scopeId, ct);

        if (agenda is null) return null;

        var accountName = agenda.AccountId is null
            ? null
            : await _db.Accounts.Where(a => a.Id == agenda.AccountId).Select(a => a.Name).FirstOrDefaultAsync(ct);

        return Project(agenda, accountName, BusinessDate.TodayWib);
    }

    internal static UpcomingEventProjection Project(UpcomingEvent agenda, string? accountName, DateOnly today)
    {
        var isScheduled = agenda.Status == UpcomingEventStatus.Scheduled;
        var isDue = isScheduled && agenda.DueDate.HasValue && agenda.DueDate.Value <= today;

        return new UpcomingEventProjection
        {
            Id = agenda.Id,
            // LEGACY ONLY — Rencana baru tidak pernah mengisi akun.
            AccountId = agenda.AccountId,
            AccountName = accountName,
            Title = agenda.Title,
            Amount = agenda.Amount,
            Direction = agenda.Direction,
            CategoryName = agenda.CategoryName,
            Note = agenda.Note,
            DueDate = agenda.DueDate,
            ScheduleKind = agenda.ScheduleKind,
            Recurrence = agenda.Recurrence,
            Status = agenda.Status,
            RealizedTransactionId = agenda.RealizedTransactionId,
            StatusReason = agenda.StatusReason,
            IsDue = isDue,
            IsOverdue = isScheduled && agenda.DueDate.HasValue && agenda.DueDate.Value < today,
            CreatedAt = agenda.CreatedAt,
            UpdatedAt = agenda.UpdatedAt
        };
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<UpcomingEvent> RequireAsync(Guid eventId, Guid scopeId, CancellationToken ct)
        => await _db.UpcomingEvents
               .FirstOrDefaultAsync(ue => ue.Id == eventId && ue.ScopeId == scopeId, ct)
           ?? throw new ValidationException("Agenda not found in current Scope.");

    private async Task<Account> RequireAccountAsync(Guid accountId, Guid scopeId, bool requireActive, CancellationToken ct)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.ScopeId == scopeId, ct)
            ?? throw new ValidationException("Account not found in current Scope.");

        if (requireActive && account.IsArchived)
            throw new ValidationException($"Cannot use archived Account '{account.Name}'.");

        return account;
    }

    private static void ValidateSchedule(UpcomingEventScheduleKind scheduleKind, DateOnly? dueDate)
    {
        if (scheduleKind == UpcomingEventScheduleKind.Scheduled && !dueDate.HasValue)
            throw new ValidationException("A scheduled agenda requires a due date.");
    }

    private static string NormalizeRequired(string value, int maxLength, string error)
    {
        var trimmed = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ValidationException(error);
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
