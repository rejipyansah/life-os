using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class SetAsideServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly SetAsideService _sut;
    private readonly AccountService _accounts;
    private readonly TransactionService _transactions;
    private readonly BalanceCalculator _balances;
    private readonly Guid _scopeId;

    public SetAsideServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _balances = new BalanceCalculator(_db);
        _sut = new SetAsideService(_db, _balances);
        _accounts = new AccountService(_db, _balances);
        _transactions = new TransactionService(_db);

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

    // ───────────────────────── 5–7: set-aside money semantics ─────────────────────────

    [Fact]
    public async Task Create_DoesNotDecreaseActualBalance()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Dana Servis Motor",
            Amount = 500_000m
        });

        // Uang yang disisihkan bukan expense: saldo riil tidak berubah.
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Create_DoesNotCreateTransaction()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var txCountBefore = await _db.Transactions.CountAsync();

        await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Dana Servis Motor",
            Amount = 500_000m
        });

        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
        Assert.Equal(txCountBefore, await _db.TransactionEntries.CountAsync());
    }

    [Fact]
    public async Task Create_DecreasesAvailableBalance()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Dana Servis Motor",
            Amount = 500_000m
        });

        Assert.Equal(500_000m, await _balances.GetActiveSetAsideAsync(account.Id));
        Assert.Equal(500_000m, await _balances.GetAvailableAsync(account.Id));
    }

    [Fact]
    public async Task Withdraw_IncreasesAvailableBalance()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 300_000m);

        var result = await _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 200_000m
        });

        Assert.Equal(100_000m, result.SetAside.Amount);
        Assert.Equal(900_000m, await _balances.GetAvailableAsync(account.Id));
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Withdraw_MoreThanReserved_Rejected()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 100_000m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand
            {
                ScopeId = _scopeId,
                Amount = 200_000m
            }));

        Assert.Contains("Insufficient set-aside balance", ex.Message);
        Assert.Equal(100_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
    }

    [Fact]
    public async Task Add_IncreasesReservedAndReducesAvailable()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Tabungan", 100_000m);

        await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand { ScopeId = _scopeId, Amount = 250_000m });

        Assert.Equal(350_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(650_000m, await _balances.GetAvailableAsync(account.Id));
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    // ───────────────────────── 8–9: target null + partial use ─────────────────────────

    [Fact]
    public async Task TargetAmount_CanBeNull()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Tabungan Organik",
            Amount = 850_000m,
            TargetAmount = null,
            Kind = SetAsideKind.Saving
        });

        Assert.Null(setAside.TargetAmount);

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Null(projected!.TargetAmount);
        Assert.Equal(850_000m, projected.Amount);
    }

    [Fact]
    public async Task Spend_PartiallyReleasesSetAside()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 300_000m);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 100_000m,
            Description = "Makan siang",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Expense benar-benar tercatat.
        Assert.NotNull(result.TransactionId);
        var tx = await _db.Transactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal(TransactionType.Expense, tx.Type);
        Assert.Equal(100_000m, tx.Amount);

        // Set-aside hanya berkurang sebagian.
        Assert.Equal(200_000m, result.SetAside.Amount);
        Assert.Equal(200_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));

        // Saldo riil berkurang sebesar pengeluaran, bukan sebesar set-aside.
        Assert.Equal(900_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(700_000m, await _balances.GetAvailableAsync(account.Id));

        // Terpakai = nominal transaksi penuh.
        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(100_000m, projected!.UsedAmount);
    }

    [Fact]
    public async Task Spend_BeyondReserved_UsesFreeMoney()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 300_000m);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 400_000m,
            Description = "Belanja besar",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // 300rb dari set-aside, 100rb dari Uang Bebas.
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(100_000m, await _balances.GetAvailableAsync(account.Id));
    }

    [Fact]
    public async Task Spend_WhenSetAsideEmpty_TakesFullAmountFromSelectedFreeCashAccount()
    {
        var posAccount = await CreateAccountAsync("SeaBank");
        var setAside = await CreateSetAsideAsync(posAccount.Id, "Dana Makan", 0m);

        var freeCash = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(freeCash.Id, 200_000m);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 50_000m,
            FreeCashAccountId = freeCash.Id,
            Description = "Makan siang warteg",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Saldo pos0 → seluruh pemakaian dari rekening Uang Bebas terpilih.
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.NotNull(result.TransactionId);
        var tx = await _db.Transactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal(TransactionType.Expense, tx.Type);
        Assert.Equal(50_000m, tx.Amount);

        Assert.Equal(0m, await _balances.GetActualBalanceAsync(posAccount.Id));
        Assert.Equal(150_000m, await _balances.GetActualBalanceAsync(freeCash.Id));
        Assert.Equal(150_000m, await _balances.GetAvailableAsync(freeCash.Id));

        // Seluruh pemakaian dari Uang Bebas tetap terhitung sebagai "Terpakai".
        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(50_000m, projected!.UsedAmount);
    }

    [Fact]
    public async Task Spend_SplitsBetweenSetAsideAndSelectedFreeCashAccount()
    {
        var posAccount = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(posAccount.Id, 100_000m);
        var setAside = await CreateSetAsideAsync(posAccount.Id, "Dana Makan", 25_000m);

        var freeCash = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(freeCash.Id, 100_000m);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 50_000m,
            FreeCashAccountId = freeCash.Id,
            Description = "Belanja campuran",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Rp25.000 dari saldo pos, Rp25.000 dari Uang Bebas.
        Assert.Equal(0m, result.SetAside.Amount);
        var tx = await _db.Transactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal(50_000m, tx.Amount);

        var entries = await _db.TransactionEntries
            .Where(te => te.TransactionId == tx.Id)
            .ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(-25_000m, entries.Single(e => e.AccountId == posAccount.Id).Amount);
        Assert.Equal(-25_000m, entries.Single(e => e.AccountId == freeCash.Id).Amount);

        Assert.Equal(75_000m, await _balances.GetActualBalanceAsync(posAccount.Id));
        Assert.Equal(75_000m, await _balances.GetActualBalanceAsync(freeCash.Id));

        // Terpakai memakai nominal penuh: porsi Uang Bebas ikut terhitung,
        // supaya pemakaian yang melewati plafon tetap terlihat.
        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(50_000m, projected!.UsedAmount);
        Assert.Equal(0m, projected.Amount);
    }

    [Fact]
    public async Task Spend_FreeCashPortion_ExceedsAvailableBalance_IsRejected()
    {
        var posAccount = await CreateAccountAsync("SeaBank");
        var setAside = await CreateSetAsideAsync(posAccount.Id, "Dana Makan", 0m);

        var freeCash = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(freeCash.Id, 10_000m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                Amount = 50_000m,
                FreeCashAccountId = freeCash.Id,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));

        Assert.Contains("Insufficient funds", ex.Message);
        Assert.Equal(0m, await _balances.GetActualBalanceAsync(posAccount.Id));
        Assert.Equal(10_000m, await _balances.GetActualBalanceAsync(freeCash.Id));
    }

    [Fact]
    public async Task Spend_NeverConsumesOtherSetAsides()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var mine = await CreateSetAsideAsync(account.Id, "Dana Makan", 300_000m);
        await CreateSetAsideAsync(account.Id, "Dana Servis", 200_000m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(mine.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                Amount = 400_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));

        Assert.Contains("Insufficient funds", ex.Message);
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(mine.Id));
    }

    [Fact]
    public async Task Spend_ReleasesEverything_WhenSpendingAll()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Kacamata", 400_000m);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 400_000m,
            Description = "Beli kacamata",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(100_000m, await _balances.GetAvailableAsync(account.Id));
    }

    // ───────────────────────── Close ─────────────────────────

    [Fact]
    public async Task Close_ReleasesRemaining_AndCreatesNoTransaction()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Tabungan", 400_000m);
        var txCountBefore = await _db.Transactions.CountAsync();

        var result = await _sut.CloseAsync(setAside.Id, new CloseSetAsideCommand
        {
            ScopeId = _scopeId,
            Reason = SetAsideCloseReason.Cancelled
        });

        Assert.Equal(SetAsideStatus.Closed, result.SetAside.Status);
        Assert.Equal(SetAsideCloseReason.Cancelled, result.SetAside.CloseReason);
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.Equal(1_000_000m, await _balances.GetAvailableAsync(account.Id));
        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Closed_SetAside_IsExcludedFromActiveTotals()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Tabungan", 400_000m);
        await _sut.CloseAsync(setAside.Id, new CloseSetAsideCommand { ScopeId = _scopeId });

        Assert.Equal(0m, await _balances.GetActiveSetAsideAsync(account.Id));
        Assert.Equal(1_000_000m, await _balances.GetAvailableAsync(account.Id));
    }

    // ───────────────────────── History ─────────────────────────

    [Fact]
    public async Task History_TracksEveryChange()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 300_000m, withInitialAmount: false);

        await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand { ScopeId = _scopeId, Amount = 300_000m, Note = "topup" });
        await _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand { ScopeId = _scopeId, Amount = 50_000m, Note = "tarik" });
        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 100_000m,
            Description = "makan",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        var history = await _sut.GetHistoryAsync(setAside.Id, _scopeId);

        Assert.Equal(3, history.Count);
        Assert.Equal(history.Sum(h => h.Amount), await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Contains(history, h => h.Type == SetAsideEntryType.Added && h.Amount == 300_000m);
        Assert.Contains(history, h => h.Type == SetAsideEntryType.Withdrawn && h.Amount == -50_000m);
        Assert.Contains(history, h => h.Type == SetAsideEntryType.Spent && h.Amount == -100_000m && h.TransactionId.HasValue);
    }

    // ───────────────────────── Scope isolation ─────────────────────────

    [Fact]
    public async Task CrossScope_SetAside_NotAccessible()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Pos A", 100_000m);

        var otherScope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand
            {
                ScopeId = otherScope.Id,
                Amount = 10_000m
            }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetHistoryAsync(setAside.Id, otherScope.Id));

        Assert.Null(await _sut.GetSetAsideAsync(setAside.Id, otherScope.Id));
    }

    [Fact]
    public async Task CrossScope_Account_Rejected()
    {
        var otherScope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var foreignAccount = new Account { ScopeId = otherScope.Id, Name = "Foreign", Type = AccountType.Bank };
        _db.Accounts.Add(foreignAccount);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                AccountId = foreignAccount.Id,
                Name = "Pos",
                Amount = 0m
            }));
    }

    // ───────────────────────── Archived account ─────────────────────────

    [Fact]
    public async Task ArchivedAccount_RejectsNewSetAside()
    {
        var account = await CreateAccountAsync("SeaBank");
        account.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                AccountId = account.Id,
                Name = "Pos",
                Amount = 0m
            }));
    }

    [Fact]
    public async Task ArchivedAccount_RejectsSpend()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 200_000m);

        await _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand { ScopeId = _scopeId, Amount = 200_000m });
        account.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                Amount = 10_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));
    }

    // ───────────────────────── Cycle: target saldo per siklus ─────────────────────────

    [Fact]
    public async Task CycleReset_Underspend_TopUpOnlyWhatIsMissing()
    {
        // Target 300rb, tersisa 200rb → hanya 100rb yang diambil dari Uang Bebas.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync(account.Id, "Dana Makan", 300_000m, SetAsideCycleKind.Monthly);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(200_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(700_000m, await _balances.GetAvailableAsync(account.Id));

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        // Tepat kembali ke target, bukan 200rb + 300rb.
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(600_000m, await _balances.GetAvailableAsync(account.Id));
        await AssertCycleFundingAsync(setAside.Id, 100_000m);
    }

    [Fact]
    public async Task CycleReset_SpentAll_RefillsToTarget()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync(account.Id, "Dana Makan", 300_000m, SetAsideCycleKind.Monthly);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 300_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        await AssertCycleFundingAsync(setAside.Id, 300_000m);
    }

    [Fact]
    public async Task CycleReset_Overspend_RefillsToTarget_AndOverspendComesFromFreeMoney()
    {
        // Plafon 300rb, pengeluaran 400rb → 100rb kelebihan ditanggung Uang Bebas.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync(account.Id, "Dana Makan", 300_000m, SetAsideCycleKind.Monthly);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 400_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(600_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(600_000m, await _balances.GetAvailableAsync(account.Id));

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        // Kembali tepat ke target — tidak pernah jadi 700rb (carry-over dilarang).
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        await AssertCycleFundingAsync(setAside.Id, 300_000m);
    }

    [Fact]
    public async Task CycleReset_NeverAccumulatesBeyondTarget()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync(account.Id, "Dana Makan", 300_000m, SetAsideCycleKind.Monthly);

        // Tidak dipakai sama sekali → saldo tetap 300rb, tidak naik jadi 600rb.
        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        await AssertNoCycleFundingAsync(setAside.Id);
    }

    [Fact]
    public async Task CycleReset_Surplus_ReturnsToFreeMoney()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync(account.Id, "Dana Makan", 300_000m, SetAsideCycleKind.Monthly);

        // Saldo di atas target, misalnya hasil koreksi.
        await SeedSetAsideEntryAsync(setAside.Id, SetAsideEntryType.Added, 150_000m);
        Assert.Equal(450_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        // Surplus 150rb dikembalikan ke Uang Bebas, saldo kembali ke target.
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(700_000m, await _balances.GetAvailableAsync(account.Id));

        var history = await _sut.GetHistoryAsync(setAside.Id, _scopeId);
        Assert.Contains(history, h => h.Type == SetAsideEntryType.Released && h.Amount == -150_000m);
    }

    [Fact]
    public async Task CycleReset_Underfunded_ReportsShortfallInsteadOfInventingMoney()
    {
        // Uang Bebas hanya 100rb, target siklus 300rb.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 100_000m);
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Dana Makan",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 0m
        });

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        // Hanya 100rb yang mampu didanai; sisanya shortfall eksplisit, bukan saldo fiktif
        // dan balance tidak pernah dibuat negatif untuk memenuhi target.
        Assert.Equal(100_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(0m, await _balances.GetAvailableAsync(account.Id));
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
        await AssertCycleFundingAsync(setAside.Id, 100_000m);

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(200_000m, projected!.TargetShortfall);
    }

    [Fact]
    public async Task CycleKind_None_IsNeverRenormalized()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Tabungan Darurat",
            Amount = 500_000m,
            TargetAmount = 5_000_000m,
            CycleKind = SetAsideCycleKind.None,
            Kind = SetAsideKind.Saving
        });

        await TouchAsync(setAside.Id);

        // Tanpa cycle, target tidak pernah menormalkan apa pun.
        Assert.Equal(500_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.False(projected!.IsCycleRolloverPending);
        Assert.Equal(0m, projected.CycleFundingRequired);
    }

    [Fact]
    public async Task CyclingSetAside_WithoutTarget_IsRejected()
    {
        var account = await CreateAccountAsync("SeaBank");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                AccountId = account.Id,
                Name = "Tanpa target",
                Amount = 0m,
                CycleKind = SetAsideCycleKind.Monthly
            }));
    }

    [Fact]
    public async Task TargetWithoutCycle_IsAllowed()
    {
        // Target dan cycle adalah dua konsep berbeda.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Tabungan Darurat",
            Amount = 500_000m,
            TargetAmount = 5_000_000m,
            CycleKind = SetAsideCycleKind.None
        });

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(5_000_000m, projected!.TargetAmount);
        Assert.Equal(SetAsideCycleKind.None, projected.CycleKind);
        Assert.False(projected.IsCycleRolloverPending);
    }

    // ───────────────────────── Concurrency ─────────────────────────

    [Fact]
    public async Task Concurrent_Withdrawals_CannotOverdraw()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 100_000m);

        var succeeded = await RunConcurrentlyAsync(8, async services =>
        {
            await services.SetAsides.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand
            {
                ScopeId = _scopeId,
                Amount = 40_000m
            });
        });

        // Hanya dua penarikan 40rb yang boleh lolos dari saldo 100rb.
        Assert.True(succeeded <= 2, $"Expected at most 2 withdrawals to succeed, got {succeeded}");
        Assert.True(await _balances.GetSetAsideAmountAsync(setAside.Id) >= 0m);
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Concurrent_SetAsides_CannotReserveMoreThanAvailable()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 100_000m);

        var succeeded = await RunConcurrentlyAsync(6, async services =>
        {
            await services.SetAsides.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                AccountId = account.Id,
                Name = "Pos " + Guid.NewGuid(),
                Amount = 40_000m
            });
        });

        Assert.True(succeeded <= 2, $"More set-asides succeeded than the available balance allows: {succeeded}");
        Assert.True(await _balances.GetAvailableAsync(account.Id) >= 0m);
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Concurrent_SpendAndWithdraw_KeepSetAsideNonNegative()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync(account.Id, "Dana Makan", 100_000m);

        var spend = Task.Run(async () =>
        {
            using var services = CreateServices();
            try
            {
                await services.SetAsides.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
                {
                    ScopeId = _scopeId,
                    Amount = 80_000m,
                    OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
                });
                return true;
            }
            catch (Exception) { return false; }
        });

        var withdraw = Task.Run(async () =>
        {
            using var services = CreateServices();
            try
            {
                await services.SetAsides.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand
                {
                    ScopeId = _scopeId,
                    Amount = 80_000m
                });
                return true;
            }
            catch (Exception) { return false; }
        });

        await Task.WhenAll(spend, withdraw);

        // Invariant: tidak ada saldo yang pernah menjadi negatif, dan setiap operasi
        // yang sukses benar-benar menggerakkan uang sesuai aturannya.
        var reserved = await _balances.GetSetAsideAmountAsync(setAside.Id);
        var available = await _balances.GetAvailableAsync(account.Id);
        var actual = await _balances.GetActualBalanceAsync(account.Id);

        Assert.True(reserved >= 0m, $"Set-aside went negative: {reserved}");
        Assert.True(available >= 0m, $"Available went negative: {available}");
        Assert.True(actual >= 0m, $"Actual balance went negative: {actual}");
        Assert.Equal(actual - reserved, available);

        // Spend menggerakkan uang riil; withdraw tidak.
        Assert.Equal(spend.Result ? 420_000m : 500_000m, actual);
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>
    /// Set of services over a fresh DbContext on the shared SQLite connection,
    /// simulating a separate HTTP request. DbContext is not thread-safe.
    /// </summary>
    private ServiceSet CreateServices()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ServiceSet(new ApplicationDbContext(options));
    }

    private async Task<int> RunConcurrentlyAsync(int count, Func<ServiceSet, Task> action)
    {
        var tasks = Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            using var services = CreateServices();
            try
            {
                await action(services);
                return true;
            }
            catch (Exception)
            {
                // SQLite cannot emulate PostgreSQL SERIALIZABLE conflicts; what matters
                // is that no attempt corrupts the money invariants.
                return false;
            }
        })).ToList();

        return (await Task.WhenAll(tasks)).Count(o => o);
    }

    private sealed class ServiceSet : IDisposable
    {
        public ServiceSet(ApplicationDbContext db)
        {
            Db = db;
            var balances = new BalanceCalculator(db);
            SetAsides = new SetAsideService(db, balances);
        }

        public ApplicationDbContext Db { get; }
        public SetAsideService SetAsides { get; }

        public void Dispose() => Db.Dispose();
    }

    private async Task<Account> CreateAccountAsync(string name)
    {
        var account = await _accounts.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = name,
            Type = AccountType.Bank
        });
        return account;
    }

    private async Task<SetAside> CreateSetAsideAsync(
        Guid accountId, string name, decimal amount, bool withInitialAmount = true)
        => await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = accountId,
            Name = name,
            Kind = SetAsideKind.Saving,
            Amount = withInitialAmount ? amount : 0m
        });

    private async Task<SetAside> CreateCyclingSetAsideAsync(
        Guid accountId, string name, decimal target, SetAsideCycleKind cycle)
        => await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            AccountId = accountId,
            Name = name,
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = target,
            CycleKind = cycle,
            Amount = target
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

    private async Task SeedSetAsideEntryAsync(Guid setAsideId, SetAsideEntryType type, decimal amount)
    {
        var setAside = await _db.SetAsides.SingleAsync(sa => sa.Id == setAsideId);
        _db.SetAsideEntries.Add(new SetAsideEntry
        {
            SetAsideId = setAsideId,
            ScopeId = setAside.ScopeId,
            Type = type,
            Amount = amount,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>Memundurkan CycleAnchorDate agar cycle dianggap sudah berganti.</summary>
    private async Task ForceCycleRolloverAsync(Guid setAsideId)
    {
        var setAside = await _db.SetAsides.SingleAsync(sa => sa.Id == setAsideId);
        setAside.CycleAnchorDate = setAside.CycleKind switch
        {
            SetAsideCycleKind.Weekly => setAside.CycleAnchorDate.AddDays(-14),
            SetAsideCycleKind.Monthly => setAside.CycleAnchorDate.AddMonths(-2),
            SetAsideCycleKind.Quarterly => setAside.CycleAnchorDate.AddMonths(-6),
            SetAsideCycleKind.SemiAnnual => setAside.CycleAnchorDate.AddMonths(-12),
            SetAsideCycleKind.Annual => setAside.CycleAnchorDate.AddYears(-2),
            _ => setAside.CycleAnchorDate
        };
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Write command yang tidak mengubah saldo disisihkan, dipakai untuk memicu
    /// normalisasi cycle yang tertunda.
    /// </summary>
    private Task TouchAsync(Guid setAsideId)
        => _sut.UpdateSetAsideAsync(setAsideId, new UpdateSetAsideCommand
        {
            ScopeId = _scopeId,
            Note = "cycle touch"
        });

    private async Task AssertCycleFundingAsync(Guid setAsideId, decimal expected)
    {
        var history = await _sut.GetHistoryAsync(setAsideId, _scopeId);
        var funding = history.Where(h => h.Type == SetAsideEntryType.CycleFunding).ToList();
        Assert.Equal(expected, funding.Sum(h => h.Amount));
    }

    private async Task AssertNoCycleFundingAsync(Guid setAsideId)
    {
        var history = await _sut.GetHistoryAsync(setAsideId, _scopeId);
        Assert.DoesNotContain(history, h => h.Type == SetAsideEntryType.CycleFunding);
    }
}
