using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class FinanceStateServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly FinanceStateService _sut;
    private readonly AccountService _accounts;
    private readonly SetAsideService _setAsides;
    private readonly UpcomingEventService _events;
    private readonly TransactionService _transactions;
    private readonly BalanceCalculator _balances;
    private readonly Guid _scopeId;

    public FinanceStateServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _balances = new BalanceCalculator(_db);
        _accounts = new AccountService(_db, _balances);
        _setAsides = new SetAsideService(_db, _balances);
        _events = new UpcomingEventService(_db, _balances);
        _transactions = new TransactionService(_db);
        _sut = new FinanceStateService(_db, _balances, _setAsides, _transactions);

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

    // ───────────────────────── 18: Uang Bebas calculation ─────────────────────────

    [Fact]
    public async Task FreeCash_IsLiquidityMinusSetAsideMinusDueObligations()
    {
        var seabank = await CreateAccountAsync("SeaBank");
        var mandiri = await CreateAccountAsync("Bank Mandiri");
        await SeedBalanceAsync(seabank.Id, 2_000_000m);
        await SeedBalanceAsync(mandiri.Id, 661_000m);

        await _setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = seabank.Id,
            Name = "Tabungan Darurat",
            Amount = 500_000m
        });

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = mandiri.Id,
            Title = "WiFi Rumah",
            Amount = 340_440m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.Equal(2_661_000m, state.TotalActualBalance);
        Assert.Equal(500_000m, state.TotalSetAside);
        Assert.Equal(340_440m, state.DueObligations);
        Assert.Equal(2_661_000m - 500_000m - 340_440m, state.FreeCash);
        Assert.Equal(1_820_560m, state.FreeCash);
    }

    [Fact]
    public async Task FreeCash_IsNeverClamped()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 100_000m);

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Sewa",
            Amount = 500_000m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.True(state.FreeCash < 0, "FreeCash should reflect the real shortfall, not be clamped to zero.");
        Assert.Equal(-400_000m, state.FreeCash);
    }

    [Fact]
    public async Task FreeCash_AccountsForPendingCycleNormalization()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Dana Makan",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 300_000m
        });

        // 100rb terpakai di cycle berjalan.
        await _setAsides.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        var before = await _sut.GetStateAsync(_scopeId);
        Assert.Equal(700_000m, before.FreeCash);

        // Cycle berganti tapi belum dinormalisasi (read tidak pernah menulis).
        var tracked = await _db.SetAsides.SingleAsync(sa => sa.Id == setAside.Id);
        tracked.CycleAnchorDate = tracked.CycleAnchorDate.AddMonths(-2);
        await _db.SaveChangesAsync();

        var after = await _sut.GetStateAsync(_scopeId);

        Assert.True(after.PendingCycleFunding > 0, "Pending cycle funding must be surfaced to the read model.");
        Assert.Equal(100_000m, after.PendingCycleFunding);
        Assert.Equal(300_000m, after.TotalCommittedSetAside);
        // 100rb dari cycle berikutnya sudah dianggap terikat, jadi Uang Bebas turun.
        Assert.Equal(600_000m, after.FreeCash);

        // Read tetap idempotent: tidak ada yang berubah di database.
        Assert.Equal(200_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
    }

    // ───────────────────────── 19: multiple accounts + multiple set-aside ─────────────────────────

    [Fact]
    public async Task MultipleAccountsAndSetAsides_AreAggregatedPerAccount()
    {
        var seabank = await CreateAccountAsync("SeaBank");
        var bca = await CreateAccountAsync("BCA");
        var cash = await CreateAccountAsync("Dompet Fisik");
        await SeedBalanceAsync(seabank.Id, 4_000_000m);
        await SeedBalanceAsync(bca.Id, 1_500_000m);
        await SeedBalanceAsync(cash.Id, 300_000m);

        await _setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId, AccountId = seabank.Id, Name = "Tabungan Darurat", Amount = 3_500_000m
        });
        await _setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId, AccountId = seabank.Id, Name = "Dana Servis Motor", Amount = 500_000m
        });
        await _setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId, AccountId = cash.Id, Name = "Dana Makan", Amount = 200_000m
        });

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.Equal(3, state.Accounts.Count);
        Assert.Equal(5_800_000m, state.TotalActualBalance);
        Assert.Equal(4_200_000m, state.TotalSetAside);
        Assert.Equal(1_600_000m, state.FreeCash);

        var seabankState = state.Accounts.Single(a => a.Id == seabank.Id);
        Assert.Equal(4_000_000m, seabankState.ActualBalance);
        Assert.Equal(4_000_000m, seabankState.SetAsideAmount);
        Assert.Equal(0m, seabankState.AvailableBalance);

        var bcaState = state.Accounts.Single(a => a.Id == bca.Id);
        Assert.Equal(1_500_000m, bcaState.AvailableBalance);
        Assert.Equal(0m, bcaState.SetAsideAmount);

        var cashState = state.Accounts.Single(a => a.Id == cash.Id);
        Assert.Equal(200_000m, cashState.SetAsideAmount);
        Assert.Equal(100_000m, cashState.AvailableBalance);

        Assert.Equal(3, state.SetAsides.Count);
    }

    // ───────────────────────── 20: no obligations = quiet state ─────────────────────────

    [Fact]
    public async Task NoObligations_ProducesQuietState()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.Equal(1_000_000m, state.TotalActualBalance);
        Assert.Equal(0m, state.TotalSetAside);
        Assert.Equal(0m, state.DueObligations);
        Assert.Equal(0, state.DueObligationsCount);
        Assert.Equal(1_000_000m, state.FreeCash);
        Assert.False(state.HasUnpaidBills);
        Assert.False(state.AllBillsPaid);
        Assert.Empty(state.UpcomingEvents);
        Assert.Empty(state.DueEvents);
        Assert.Empty(state.SetAsides);
    }

    [Fact]
    public async Task PostponedEvent_IsNoLongerADueObligation_ButStillCommitsFreeCash()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var agenda = await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "WiFi Rumah",
            Amount = 340_440m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        Assert.Equal(340_440m, (await _sut.GetStateAsync(_scopeId)).DueObligations);

        await _events.PostponeAsync(agenda.Id, new PostponeUpcomingEventCommand { ScopeId = _scopeId });

        var state = await _sut.GetStateAsync(_scopeId);
        // Keluar dari Jatuh Tempo…
        Assert.Equal(0m, state.DueObligations);
        // …tapi tetap direncanakan, jadi Uang Bebas masih terikat.
        Assert.Equal(340_440m, state.ScheduledExpenseCommitments);
        Assert.Equal(659_560m, state.FreeCash);
        Assert.Single(state.UpcomingEvents);
    }

    [Fact]
    public async Task FutureEvent_ReducesFreeCash_AsPlannedCommitment()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Sewa",
            Amount = 1_200_000m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        var state = await _sut.GetStateAsync(_scopeId);
        // Belum jatuh tempo — tidak muncul di Jatuh Tempo…
        Assert.Equal(0m, state.DueObligations);
        // …tapi tetap mengikat Uang Bebas sebagai rencana, mirip disisihkan.
        Assert.Equal(1_200_000m, state.ScheduledExpenseCommitments);
        Assert.Equal(-200_000m, state.FreeCash);
        Assert.Single(state.UpcomingEvents);
        Assert.DoesNotContain(state.RecentTransactions, t => t.Description == "Sewa");
    }

    [Fact]
    public async Task CyclingExpense_AlwaysReducesFreeCash_EvenBeforeDueDate()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Internet Bulanan",
            Amount = 350_000m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled,
            Recurrence = UpcomingEventRecurrence.Monthly
        });

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.Equal(0m, state.DueObligations);
        Assert.Equal(350_000m, state.ScheduledExpenseCommitments);
        // Mirip disisihkan: Uang Bebas turun, tapi belum jadi transaksi.
        Assert.Equal(650_000m, state.FreeCash);
        Assert.DoesNotContain(state.RecentTransactions, t => t.Description == "Internet Bulanan");
        Assert.Single(state.RecentTransactions);
    }

    [Fact]
    public async Task ExpenseDueToday_DoesNotDoubleCountInFreeCash()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Internet Bulanan",
            Amount = 350_000m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled,
            Recurrence = UpcomingEventRecurrence.Monthly
        });

        var state = await _sut.GetStateAsync(_scopeId);

        // Jatuh Tempo tetap menampilkan tagihan ini…
        Assert.Equal(350_000m, state.DueObligations);
        // …tapi freeCash hanya mengurangi sekali lewat ScheduledExpenseCommitments.
        Assert.Equal(350_000m, state.ScheduledExpenseCommitments);
        Assert.Equal(650_000m, state.FreeCash);
    }

    [Fact]
    public async Task CyclingIncome_NeverReducesFreeCash()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Gaji Bulanan",
            Amount = 5_000_000m,
            Direction = UpcomingEventDirection.Income,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled,
            Recurrence = UpcomingEventRecurrence.Monthly
        });

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.Equal(0m, state.ScheduledExpenseCommitments);
        Assert.Equal(1_000_000m, state.FreeCash);
    }

    [Fact]
    public async Task UpcomingIncome_NeverCountsAsObligation()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Gaji",
            Amount = 5_000_000m,
            Direction = UpcomingEventDirection.Income,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        var state = await _sut.GetStateAsync(_scopeId);
        Assert.Equal(0m, state.DueObligations);
        Assert.Equal(1_000_000m, state.FreeCash);
    }

    [Fact]
    public async Task RecentTransactions_ContainRealMovementsOnly()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        // Agenda direncanakan — bukan mutasi riil.
        await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "Belum terjadi",
            Amount = 50_000m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        var state = await _sut.GetStateAsync(_scopeId);

        Assert.Single(state.RecentTransactions);
        Assert.DoesNotContain(state.RecentTransactions, t => t.Description == "Belum terjadi");
    }

    [Fact]
    public async Task AllBillsPaid_IsTrueOnlyWhenObligationsExistedAndAreGone()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var empty = await _sut.GetStateAsync(_scopeId);
        Assert.False(empty.AllBillsPaid);

        var agenda = await _events.CreateAsync(new CreateUpcomingEventCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Title = "WiFi Rumah",
            Amount = 340_440m,
            Direction = UpcomingEventDirection.Expense,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ScheduleKind = UpcomingEventScheduleKind.Scheduled
        });

        Assert.True((await _sut.GetStateAsync(_scopeId)).HasUnpaidBills);

        await _events.RealizeAsync(agenda.Id, new RealizeUpcomingEventCommand { ScopeId = _scopeId });

        var state = await _sut.GetStateAsync(_scopeId);
        Assert.False(state.HasUnpaidBills);
        Assert.True(state.AllBillsPaid);
    }

    [Fact]
    public async Task Read_IsIdempotent_AndWritesNothing()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        await _setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId, AccountId = account.Id, Name = "Pos", Amount = 100_000m
        });

        var entriesBefore = await _db.SetAsideEntries.CountAsync();
        var txBefore = await _db.TransactionEntries.CountAsync();

        var first = await _sut.GetStateAsync(_scopeId);
        var second = await _sut.GetStateAsync(_scopeId);

        Assert.Equal(first.FreeCash, second.FreeCash);
        Assert.Equal(entriesBefore, await _db.SetAsideEntries.CountAsync());
        Assert.Equal(txBefore, await _db.TransactionEntries.CountAsync());
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<Account> CreateAccountAsync(string name)
        => await _accounts.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = name,
            Type = AccountType.Bank
        });

    private async Task SeedBalanceAsync(Guid accountId, decimal amount)
    {
        var tx = new Transaction
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
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
