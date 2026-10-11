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
    private const decimal MaxAgendaAmount = 9_007_199_254_740_991m;

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
        if (command.Amount > MaxAgendaAmount)
            throw new ValidationException($"Agenda amount cannot exceed {MaxAgendaAmount:N0}.");

        if (!Enum.IsDefined(command.Direction))
            throw new ValidationException($"Invalid direction: {command.Direction}");
        if (!Enum.IsDefined(command.ScheduleKind))
            throw new ValidationException($"Invalid schedule kind: {command.ScheduleKind}");
        if (!Enum.IsDefined(command.Recurrence))
            throw new ValidationException($"Invalid recurrence: {command.Recurrence}");

        ValidateSchedule(command.ScheduleKind, command.DueDate);
        ValidateRecurrence(command.ScheduleKind, command.Recurrence);
        ValidateNotPast(command.ScheduleKind, command.DueDate);

        if (command.DefaultSourceAccountId.HasValue)
            await RequireAccountAsync(command.DefaultSourceAccountId.Value, command.ScopeId, requireActive: true, ct);

        var now = DateTime.UtcNow;
        var agenda = new UpcomingEvent
        {
            ScopeId = command.ScopeId,
            // AccountId legacy tidak ditulis; default source hanya preferensi realisasi.
            AccountId = null,
            DefaultSourceAccountId = command.DefaultSourceAccountId,
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
            if (command.Amount.Value > MaxAgendaAmount)
                throw new ValidationException($"Agenda amount cannot exceed {MaxAgendaAmount:N0}.");
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

        if (command.DefaultSourceAccountId.HasValue)
        {
            await RequireAccountAsync(command.DefaultSourceAccountId.Value, command.ScopeId, requireActive: true, ct);
            agenda.DefaultSourceAccountId = command.DefaultSourceAccountId.Value;
        }

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
        ValidateRecurrence(agenda.ScheduleKind, agenda.Recurrence);
        if (command.DueDate.HasValue)
            ValidateNotPast(agenda.ScheduleKind, agenda.DueDate);
        if (agenda.ScheduleKind == UpcomingEventScheduleKind.Flexible)
            agenda.DueDate = null;

        agenda.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return agenda;
    }

    // ───────────────────────── Lifecycle ─────────────────────────

    /// <summary>Menunda agenda ke tanggal berikutnya tanpa mengubah data lainnya.</summary>
    public Task<UpcomingEvent> PostponeAsync(Guid eventId, PostponeUpcomingEventCommand command, CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => PostponeInternalAsync(eventId, command, token), ct);

    private async Task<UpcomingEvent> PostponeInternalAsync(Guid eventId, PostponeUpcomingEventCommand command, CancellationToken ct)
    {
        var agenda = await RequireAsync(eventId, command.ScopeId, ct);
        if (agenda.Status != UpcomingEventStatus.Scheduled)
            throw new ValidationException("Only a scheduled agenda can be postponed.");

        if (agenda.ScheduleKind == UpcomingEventScheduleKind.Flexible && !command.NewDueDate.HasValue)
            throw new ValidationException("A flexible agenda has no date to postpone. Provide a new due date.");

        var newDate = command.NewDueDate ?? (agenda.DueDate ?? BusinessDate.TodayWib).AddDays(1);

        if (agenda.ScheduleKind == UpcomingEventScheduleKind.Flexible && newDate < BusinessDate.TodayWib)
            throw new ValidationException("A flexible agenda must be postponed to today or a future date.");

        if (agenda.DueDate.HasValue && newDate < agenda.DueDate.Value)
            throw new ValidationException("Postponing must move the due date forward.");

        agenda.DueDate = newDate;
        agenda.ScheduleKind = UpcomingEventScheduleKind.Scheduled;
        agenda.StatusReason = NormalizeOptional(command.Reason, 512);
        agenda.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return agenda;
    }

    /// <summary>Lewati kejadian siklus ini dan jadwalkan kejadian siklus berikutnya.</summary>
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

            if (status == UpcomingEventStatus.Skipped)
                AddNextOccurrence(agenda);

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

        AddNextOccurrence(agenda);

        await _db.SaveChangesAsync(ct);

        return new RealizeUpcomingEventResult
        {
            Event = Project(agenda, null,
                agenda.DefaultSourceAccountId == account.Id ? account.Name : null,
                account.Id,
                account.Name,
                BusinessDate.TodayWib),
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
        var accountIds = all.SelectMany(ue => new[] { ue.AccountId, ue.DefaultSourceAccountId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts
                .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var realizedTransactionIds = all
            .Where(ue => ue.RealizedTransactionId.HasValue)
            .Select(ue => ue.RealizedTransactionId!.Value)
            .Distinct()
            .ToList();
        var realizedSources = realizedTransactionIds.Count == 0
            ? []
            : await _db.TransactionEntries
                .Where(te => realizedTransactionIds.Contains(te.TransactionId))
                .Select(te => new { te.TransactionId, te.AccountId, AccountName = te.Account.Name })
                .ToListAsync(ct);
        var realizedSourceByTransaction = realizedSources
            .GroupBy(source => source.TransactionId)
            .ToDictionary(group => group.Key, group => group.First());

        var today = BusinessDate.TodayWib;
        return all
            .OrderBy(ue => ue.Status == UpcomingEventStatus.Scheduled ? 0 : 1)
            .ThenBy(ue => ue.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(ue => ue.CreatedAt)
            .Select(ue => Project(ue,
                ue.AccountId is null ? null : accountNames.GetValueOrDefault(ue.AccountId.Value),
                ue.DefaultSourceAccountId is null ? null : accountNames.GetValueOrDefault(ue.DefaultSourceAccountId.Value),
                ue.RealizedTransactionId is { } transactionId && realizedSourceByTransaction.TryGetValue(transactionId, out var source)
                    ? source.AccountId
                    : null,
                ue.RealizedTransactionId is { } realizedId && realizedSourceByTransaction.TryGetValue(realizedId, out var realizedSource)
                    ? realizedSource.AccountName
                    : null,
                today))
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

        var defaultSourceAccountName = agenda.DefaultSourceAccountId is null
            ? null
            : await _db.Accounts.Where(a => a.Id == agenda.DefaultSourceAccountId).Select(a => a.Name).FirstOrDefaultAsync(ct);

        Guid? realizedSourceAccountId = null;
        string? realizedSourceAccountName = null;
        if (agenda.RealizedTransactionId.HasValue)
        {
            var realizedSource = await _db.TransactionEntries
                .Where(te => te.TransactionId == agenda.RealizedTransactionId.Value)
                .Select(te => new { te.AccountId, AccountName = te.Account.Name })
                .FirstOrDefaultAsync(ct);
            realizedSourceAccountId = realizedSource?.AccountId;
            realizedSourceAccountName = realizedSource?.AccountName;
        }

        return Project(agenda, accountName, defaultSourceAccountName,
            realizedSourceAccountId, realizedSourceAccountName, BusinessDate.TodayWib);
    }

    internal static UpcomingEventProjection Project(
        UpcomingEvent agenda,
        string? accountName,
        string? defaultSourceAccountName,
        Guid? realizedSourceAccountId,
        string? realizedSourceAccountName,
        DateOnly today)
    {
        var isScheduled = agenda.Status == UpcomingEventStatus.Scheduled;
        var isDue = isScheduled && agenda.DueDate.HasValue && agenda.DueDate.Value <= today;
        var commitmentStartDate = agenda.ScheduleKind == UpcomingEventScheduleKind.Scheduled && agenda.DueDate.HasValue
            ? GetCommitmentStartDate(agenda.DueDate.Value, agenda.Recurrence, agenda.CreatedAt)
            : (DateOnly?)null;

        return new UpcomingEventProjection
        {
            Id = agenda.Id,
            // AccountId legacy; DefaultSourceAccountId is a non-binding preference.
            AccountId = agenda.AccountId,
            AccountName = accountName,
            DefaultSourceAccountId = agenda.DefaultSourceAccountId,
            DefaultSourceAccountName = defaultSourceAccountName,
            RealizedSourceAccountId = realizedSourceAccountId,
            RealizedSourceAccountName = realizedSourceAccountName,
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
            CommitmentStartDate = commitmentStartDate,
            IsCommitmentActive = isScheduled
                && agenda.Direction == UpcomingEventDirection.Expense
                && commitmentStartDate.HasValue
                && commitmentStartDate.Value <= today,
            CreatedAt = agenda.CreatedAt,
            UpdatedAt = agenda.UpdatedAt
        };
    }

    public static DateOnly GetCommitmentStartDate(
        DateOnly dueDate, UpcomingEventRecurrence recurrence, DateTime createdAtUtc)
        => recurrence switch
        {
            UpcomingEventRecurrence.Weekly => dueDate.AddDays(-(((int)dueDate.DayOfWeek + 6) % 7)),
            UpcomingEventRecurrence.Monthly => new DateOnly(dueDate.Year, dueDate.Month, 1),
            UpcomingEventRecurrence.Quarterly => new DateOnly(dueDate.Year, ((dueDate.Month - 1) / 3 * 3) + 1, 1),
            UpcomingEventRecurrence.Annual => new DateOnly(dueDate.Year, 1, 1),
            _ => DateOnly.FromDateTime(createdAtUtc.AddHours(7))
        };

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

    private static void ValidateNotPast(UpcomingEventScheduleKind scheduleKind, DateOnly? dueDate)
    {
        if (scheduleKind == UpcomingEventScheduleKind.Scheduled
            && dueDate.HasValue
            && dueDate.Value < BusinessDate.TodayWib)
        {
            throw new ValidationException("A scheduled agenda date cannot be before today.");
        }
    }

    private static void ValidateRecurrence(
        UpcomingEventScheduleKind scheduleKind,
        UpcomingEventRecurrence recurrence)
    {
        if (recurrence != UpcomingEventRecurrence.None
            && scheduleKind != UpcomingEventScheduleKind.Scheduled)
        {
            throw new ValidationException("A recurring agenda must have a scheduled due date.");
        }
    }

    private void AddNextOccurrence(UpcomingEvent agenda)
    {
        if (agenda.Recurrence == UpcomingEventRecurrence.None
            || agenda.ScheduleKind != UpcomingEventScheduleKind.Scheduled
            || !agenda.DueDate.HasValue)
        {
            return;
        }

        var nextDueDate = agenda.Recurrence switch
        {
            UpcomingEventRecurrence.Weekly => agenda.DueDate.Value.AddDays(7),
            UpcomingEventRecurrence.Monthly => agenda.DueDate.Value.AddMonths(1),
            UpcomingEventRecurrence.Quarterly => agenda.DueDate.Value.AddMonths(3),
            UpcomingEventRecurrence.Annual => agenda.DueDate.Value.AddYears(1),
            _ => (DateOnly?)null
        };

        if (!nextDueDate.HasValue) return;

        var now = DateTime.UtcNow;
        _db.UpcomingEvents.Add(new UpcomingEvent
        {
            ScopeId = agenda.ScopeId,
            AccountId = null,
            DefaultSourceAccountId = agenda.DefaultSourceAccountId,
            Title = agenda.Title,
            Amount = agenda.Amount,
            Direction = agenda.Direction,
            CategoryName = agenda.CategoryName,
            Note = agenda.Note,
            DueDate = nextDueDate,
            ScheduleKind = UpcomingEventScheduleKind.Scheduled,
            Recurrence = agenda.Recurrence,
            Status = UpcomingEventStatus.Scheduled,
            CreatedAt = now,
            UpdatedAt = now
        });
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
