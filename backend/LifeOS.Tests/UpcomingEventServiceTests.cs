using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class UpcomingEventServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly UpcomingEventService _sut;
    private readonly AccountService _accounts;
    private readonly BalanceCalculator _balances;
    private readonly Guid _scopeId;

    public UpcomingEventServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _balances = new BalanceCalculator(_db);
        _sut = new UpcomingEventService(_db, _balances);
        _accounts = new AccountService(_db, _balances);

        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        _db.SaveChanges();
        _scopeId = scope.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ───────────────────────── 10–11: expected events never move money ─────────────────────────

    [Fact]
    public async Task UpcomingIncome_DoesNotIncreaseActualBalance()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 500_000m);
        var txCountBefore = await _db.Transactions.CountAsync();

        await _sut.CreateAsync(NewIncome("Gaji", 5_000_000m, account.Id));

        Assert.Equal(500_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task UpcomingExpense_DoesNotDecreaseActualBalance()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 2_000_000m);

        await _sut.CreateAsync(NewExpense("WiFi Rumah", 340_440m, account.Id));

        Assert.Equal(2_000_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(0, await _db.Transactions.CountAsync(t => t.Description == "WiFi Rumah"));
    }

    [Fact]
    public async Task PassingTheDueDate_DoesNotTurnEventIntoTransaction()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 2_000_000m);

        var agenda = await _sut.CreateAsync(NewExpense("Sewa", 1_200_000m, account.Id,
            dueDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3)));

        var projected = await _sut.GetByIdAsync(agenda.Id, _scopeId);

        // Tanggalnya sudah lewat, tapi belum direalisasikan → bukan transaksi.
        Assert.True(projected!.IsDue);
        Assert.True(projected.IsOverdue);
        Assert.Equal(UpcomingEventStatus.Scheduled, projected.Status);
        Assert.Equal(2_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    // ───────────────────────── 12–13: realizing creates a real transaction ─────────────────────────

    [Fact]
    public async Task Realize_Income_CreatesActualTransaction()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 500_000m);

        var agenda = await _sut.CreateAsync(NewIncome("Gaji", 5_000_000m, account.Id));
        var result = await _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId });

        var tx = await _db.Transactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal(TransactionType.Income, tx.Type);
        Assert.Equal(5_000_000m, tx.Amount);
        Assert.Equal("Gaji", tx.Description);

        var entry = await _db.TransactionEntries.SingleAsync(te => te.TransactionId == tx.Id);
        Assert.Equal(account.Id, entry.AccountId);
        Assert.Equal(5_000_000m, entry.Amount);

        Assert.Equal(5_500_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(UpcomingEventStatus.Realized, result.Event.Status);
        Assert.Equal(result.TransactionId, result.Event.RealizedTransactionId);
    }

    [Fact]
    public async Task Realize_Expense_CreatesActualTransaction()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 2_000_000m);

        var agenda = await _sut.CreateAsync(NewExpense("WiFi Rumah", 340_440m, account.Id));
        var result = await _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId });

        var tx = await _db.Transactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal(TransactionType.Expense, tx.Type);
        Assert.Equal(340_440m, tx.Amount);

        var entry = await _db.TransactionEntries.SingleAsync(te => te.TransactionId == tx.Id);
        Assert.Equal(-340_440m, entry.Amount);

        Assert.Equal(1_659_560m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Realize_Twice_IsRejected()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 2_000_000m);
        var txCountBefore = await _db.Transactions.CountAsync();

        var agenda = await _sut.CreateAsync(NewExpense("WiFi Rumah", 340_440m, account.Id));
        await _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId }));

        // Hanya satu transaksi riil yang tercipta.
        Assert.Equal(txCountBefore + 1, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Realize_Expense_WithoutBalance_IsRejected()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 100_000m);
        var txCountBefore = await _db.Transactions.CountAsync();

        var agenda = await _sut.CreateAsync(NewExpense("Sewa", 1_200_000m, account.Id));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId }));

        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Realize_Expense_RespectsReservedFunds()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAsides = new SetAsideService(_db, _balances);
        await setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Tabungan",
            Amount = 900_000m
        });

        var agenda = await _sut.CreateAsync(NewExpense("Sewa", 500_000m, account.Id));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId }));

        Assert.Contains("Insufficient available balance", ex.Message);
    }

    // ───────────────────────── Lifecycle ─────────────────────────

    [Fact]
    public async Task Postpone_MovesDueDateForward()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, await CreateAccountIdAsync("Mandiri"), today));

        await _sut.PostponeAsync(agenda.Id, new PostponeUpcomingEventCommand { ScopeId = _scopeId });

        var projected = await _sut.GetByIdAsync(agenda.Id, _scopeId);
        Assert.Equal(today.AddDays(1), projected!.DueDate);
        Assert.Equal(UpcomingEventStatus.Scheduled, projected.Status);
    }

    [Fact]
    public async Task Postpone_ExplicitDate_IsApplied()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, await CreateAccountIdAsync("Mandiri"), today));
        var nextWeek = today.AddDays(7);

        await _sut.PostponeAsync(agenda.Id, new PostponeUpcomingEventCommand { ScopeId = _scopeId, NewDueDate = nextWeek });

        Assert.Equal(nextWeek, (await _sut.GetByIdAsync(agenda.Id, _scopeId))!.DueDate);
    }

    [Fact]
    public async Task Postpone_Backwards_IsRejected()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, await CreateAccountIdAsync("Mandiri"), today));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.PostponeAsync(agenda.Id, new PostponeUpcomingEventCommand
            {
                ScopeId = _scopeId,
                NewDueDate = today.AddDays(-1)
            }));
    }

    [Fact]
    public async Task Skip_MovesNoMoney_AndIsTerminal()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var agenda = await _sut.CreateAsync(NewExpense("Donasi", 100_000m, account.Id));

        await _sut.SkipAsync(agenda.Id, new SettleUpcomingEventCommand { ScopeId = _scopeId, Reason = "Dilewati" });

        var projected = await _sut.GetByIdAsync(agenda.Id, _scopeId);
        Assert.Equal(UpcomingEventStatus.Skipped, projected!.Status);
        Assert.Equal("Dilewati", projected.StatusReason);
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId }));
    }

    [Fact]
    public async Task Cancel_MovesNoMoney()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var agenda = await _sut.CreateAsync(NewExpense("Langganan", 50_000m, account.Id));

        await _sut.CancelAsync(agenda.Id, new SettleUpcomingEventCommand { ScopeId = _scopeId });

        Assert.Equal(UpcomingEventStatus.Cancelled, (await _sut.GetByIdAsync(agenda.Id, _scopeId))!.Status);
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Delete_RemovesScheduledEvent()
    {
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, await CreateAccountIdAsync("Mandiri")));

        await _sut.DeleteAsync(agenda.Id, _scopeId);

        Assert.Null(await _sut.GetByIdAsync(agenda.Id, _scopeId));
    }

    [Fact]
    public async Task Delete_RealizedEvent_IsRejected_ToPreserveAuditTrail()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, account.Id));
        await _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.DeleteAsync(agenda.Id, _scopeId));
        Assert.Contains("cannot be deleted", ex.Message);

        // Histori tetap dapat ditelusuri dari transaksinya.
        var projected = await _sut.GetByIdAsync(agenda.Id, _scopeId);
        Assert.NotNull(projected!.RealizedTransactionId);
    }

    // ───────────────────────── Scope & archive ─────────────────────────

    [Fact]
    public async Task CrossScope_Agenda_NotAccessible()
    {
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, await CreateAccountIdAsync("Mandiri")));

        var otherScope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        Assert.Null(await _sut.GetByIdAsync(agenda.Id, otherScope.Id));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SkipAsync(agenda.Id, new SettleUpcomingEventCommand { ScopeId = otherScope.Id }));
    }

    [Fact]
    public async Task ArchivedAccount_CannotBeRealized()
    {
        var account = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var agenda = await _sut.CreateAsync(NewExpense("WiFi", 340_440m, account.Id));

        await _sut.PostponeAsync(agenda.Id, new PostponeUpcomingEventCommand { ScopeId = _scopeId });
        // Withdraw the balance first so the account can be archived.
        await SeedBalanceAsync(account.Id, -1_000_000m);
        account.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId }));
    }

    [Fact]
    public async Task ArchivedAccount_CannotReceiveNewAgenda()
    {
        var account = await CreateAccountAsync("Mandiri");
        account.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAsync(NewExpense("WiFi", 340_440m, account.Id)));
    }

    // ───────────────────────── Validation ─────────────────────────

    [Fact]
    public async Task Scheduled_WithoutDueDate_IsRejected()
    {
        var accountId = await CreateAccountIdAsync("Mandiri");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAsync(new CreateUpcomingEventCommand
            {
                ScopeId = _scopeId,
                AccountId = accountId,
                Title = "Tanpa tanggal",
                Amount = 10_000m,
                Direction = UpcomingEventDirection.Expense,
                ScheduleKind = UpcomingEventScheduleKind.Scheduled,
                DueDate = null
            }));
    }

    [Fact]
    public async Task Flexible_WithoutDueDate_IsAllowed()
    {
        var accountId = await CreateAccountIdAsync("Mandiri");

        var agenda = await _sut.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = accountId,
            Title = "Sembako",
            Amount = 200_000m,
            Direction = UpcomingEventDirection.Expense,
            ScheduleKind = UpcomingEventScheduleKind.Flexible,
            DueDate = null
        });

        var projected = await _sut.GetByIdAsync(agenda.Id, _scopeId);
        Assert.Null(projected!.DueDate);
        Assert.False(projected.IsDue);
    }

    [Fact]
    public async Task ZeroAmount_IsRejected()
    {
        var accountId = await CreateAccountIdAsync("Mandiri");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAsync(NewExpense("WiFi", 0m, accountId)));
    }

    [Fact]
    public async Task Realize_WithoutAccount_IsRejected()
    {
        var agenda = await _sut.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            Title = "Belum ada sumber dana",
            Amount = 10_000m,
            Direction = UpcomingEventDirection.Expense,
            ScheduleKind = UpcomingEventScheduleKind.Flexible
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId }));
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<Account> CreateAccountAsync(string name)
        => await _accounts.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = name,
            Type = AccountType.Bank
        });

    private async Task<Guid> CreateAccountIdAsync(string name)
        => (await CreateAccountAsync(name)).Id;

    private CreateUpcomingEventCommand NewExpense(
        string title, decimal amount, Guid? accountId, DateOnly? dueDate = null)
        => new()
        {
            ScopeId = _scopeId,
            AccountId = accountId,
            Title = title,
            Amount = amount,
            Direction = UpcomingEventDirection.Expense,
            CategoryName = "Rutin",
            DueDate = dueDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        };

    private CreateUpcomingEventCommand NewIncome(
        string title, decimal amount, Guid? accountId, DateOnly? dueDate = null)
        => new()
        {
            ScopeId = _scopeId,
            AccountId = accountId,
            Title = title,
            Amount = amount,
            Direction = UpcomingEventDirection.Income,
            CategoryName = "Pemasukan",
            DueDate = dueDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        };

    private async Task SeedBalanceAsync(Guid accountId, decimal amount)
    {
        var tx = new Transaction
        {
            ScopeId = _scopeId,
            Type = amount >= 0 ? TransactionType.Income : TransactionType.Expense,
            Amount = Math.Abs(amount),
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        _db.TransactionEntries.Add(new TransactionEntry
        {
            TransactionId = tx.Id,
            AccountId = accountId,
            Amount = amount
        });
        await _db.SaveChangesAsync();
    }
}
