using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

/// <summary>
/// Dana yang Disisihkan = pool alokasi scope-wide, INDEPENDEN dari Sumber Dana.
/// SetAside.AccountId legacy tidak dipakai untuk perhitungan apa pun.
/// SourceAccountId hanya menjadi referensi akun default, bukan batas saldo.
/// </summary>
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

    // ───────────────────────── Set-aside money semantics ─────────────────────────

    [Fact]
    public async Task Create_DoesNotDecreaseActualBalance()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
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
            SourceAccountId = account.Id,
            Name = "Dana Servis Motor",
            Amount = 500_000m
        });

        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
        Assert.Equal(txCountBefore, await _db.TransactionEntries.CountAsync());
    }

    [Fact]
    public async Task Create_DecreasesScopeAvailable_NotPerAccount()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Name = "Dana Servis Motor",
            Amount = 500_000m
        });

        // Alokasi mengurangi TotalAvailable scope-wide, bukan saldo per akun.
        Assert.Equal(500_000m, await _balances.GetActiveSetAsideTotalAsync(_scopeId));
        Assert.Equal(500_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        // Saldo aktual akun tidak berubah — alokasi bukan milik akun.
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));

        // Pos baru TIDAK terikat ke akun mana pun.
        Assert.Null(setAside.AccountId);
    }

    [Fact]
    public async Task Create_WithoutSourceAccount_IsAllowed()
    {
        // Pos tidak pernah terikat Sumber Dana — tanpa pendanaan awal, tanpa akun.
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Dana Pacaran",
            Amount = 0m
        });

        Assert.Null(setAside.AccountId);
        Assert.Null(setAside.DefaultSourceAccountId);
        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));
    }

    [Fact]
    public async Task Create_StoresDefaultSourceAccountAsNonBindingHint()
    {
        // SourceAccountId disimpan sebagai hint default untuk proses manual —
        // bukan kepemilikan pos, tidak dipakai untuk perhitungan.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Name = "Dana Servis Motor",
            Amount = 500_000m
        });

        Assert.Null(setAside.AccountId);
        Assert.Equal(account.Id, setAside.DefaultSourceAccountId);

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(account.Id, projected!.DefaultSourceAccountId);
        Assert.Equal("SeaBank", projected.DefaultSourceAccountName);

        // Hint tidak memengaruhi perhitungan alokasi.
        Assert.Equal(500_000m, projected.Amount);
        Assert.Equal(500_000m, await _balances.GetActiveSetAsideTotalAsync(_scopeId));
    }

    [Fact]
    public async Task Create_WithInitialAmount_WithoutSourceAccount_ValidatesScopeAvailableOnly()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        // Tanpa SourceAccountId: hanya cek TotalAvailable scope-wide.
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Tabungan",
            Amount = 400_000m
        });

        Assert.Null(setAside.AccountId);
        Assert.Equal(600_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    [Fact]
    public async Task Create_InsufficientFreeCash_Rejected()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 300_000m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                Name = "Pos Terlalu Besar",
                Amount = 500_000m
            }));

        Assert.Contains("Uang Bebas tidak cukup", ex.Message);
    }

    [Fact]
    public async Task Create_ReferenceAccountMayHaveZeroBalance_WhenScopeFreeCashIsSufficient()
    {
        var referenceAccount = await CreateAccountAsync("BCA Digital");
        var otherAccount = await CreateAccountAsync("Rekening Harian");
        await SeedBalanceAsync(otherAccount.Id, 500_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = referenceAccount.Id,
            Name = "Tabungan",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 300_000m
        });

        Assert.Equal(referenceAccount.Id, setAside.DefaultSourceAccountId);
        Assert.Equal(0m, await _balances.GetActualBalanceAsync(referenceAccount.Id));
        Assert.Equal(0m, setAside.CycleFundingShortfall);
        Assert.Equal(200_000m, await _balances.GetScopeFreeCashAsync(_scopeId));

        var toppedUp = await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = referenceAccount.Id,
            Amount = 100_000m
        });
        Assert.Equal(400_000m, toppedUp.SetAside.Amount);
        Assert.Equal(referenceAccount.Id, toppedUp.SetAside.DefaultSourceAccountId);
        Assert.Equal(0m, await _balances.GetActualBalanceAsync(referenceAccount.Id));
        Assert.Equal(100_000m, await _balances.GetScopeFreeCashAsync(_scopeId));
    }

    [Fact]
    public async Task Add_WhenFreeCashIsZero_IsRejectedEvenIfTotalAvailableIsPositive()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Tabungan", 0m);
        _db.UpcomingEvents.Add(new UpcomingEvent
        {
            ScopeId = _scopeId,
            Title = "Tagihan terjadwal",
            Amount = 500_000m,
            Direction = UpcomingEventDirection.Expense,
            Status = UpcomingEventStatus.Scheduled
        });
        await _db.SaveChangesAsync();

        Assert.Equal(500_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        Assert.Equal(0m, await _balances.GetScopeFreeCashAsync(_scopeId));
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.AddAsync(setAside.Id, new AddToSetAsideCommand
            {
                ScopeId = _scopeId,
                Amount = 1_000m
            }));

        Assert.Contains("Uang Bebas tidak cukup", exception.Message);
        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));
    }

    [Fact]
    public async Task Withdraw_IncreasesScopeAvailable()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 300_000m, sourceAccountId: account.Id);

        var result = await _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 200_000m
        });

        Assert.Equal(100_000m, result.SetAside.Amount);
        Assert.Equal(900_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Withdraw_MoreThanReserved_Rejected()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 100_000m, sourceAccountId: account.Id);

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
    public async Task Add_IncreasesReservedAndReducesScopeAvailable()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Tabungan", 100_000m, sourceAccountId: account.Id);

        await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 250_000m
        });

        Assert.Equal(350_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(650_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Add_WithoutSourceAccount_UsesScopeAvailable()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Tabungan", 100_000m, sourceAccountId: account.Id);

        await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand { ScopeId = _scopeId, Amount = 200_000m });

        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(200_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    // ───────────────────────── Target null + partial use ─────────────────────────

    [Fact]
    public async Task TargetAmount_CanBeNull()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
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
        var setAside = await CreateSetAsideAsync("Dana Makan", 300_000m, sourceAccountId: account.Id);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
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
        // TotalAvailable = 900rb − 200rb.
        Assert.Equal(700_000m, await _balances.GetScopeAvailableAsync(_scopeId));

        // Terpakai = nominal transaksi penuh.
        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(100_000m, projected!.UsedAmount);
    }

    [Fact]
    public async Task Spend_BeyondReserved_UsesFreeMoney()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 300_000m, sourceAccountId: account.Id);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 400_000m,
            Description = "Belanja besar",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // 300rb dari pos, 100rb dari uang yang belum dialokasikan.
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(100_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    [Fact]
    public async Task Spend_FromDifferentAccountThanPosCreation()
    {
        // POS TIDAK TERIKAT AKUN: dibuat "dari SeaBank", tapi spend bisa dari akun lain.
        var seabank = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(seabank.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Dana Pacaran", 500_000m, sourceAccountId: seabank.Id);

        var cash = await CreateAccountAsync("Tunai");
        await SeedBalanceAsync(cash.Id, 200_000m);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = cash.Id,
            Amount = 200_000m,
            Description = "Kopi sama pacar",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Uang keluar dari Tunai (Sumber Dana aktual), bukan dari "akun pos".
        Assert.NotNull(result.TransactionId);
        var entries = await _db.TransactionEntries
            .Where(te => te.TransactionId == result.TransactionId)
            .ToListAsync();
        Assert.Single(entries);
        Assert.Equal(cash.Id, entries[0].AccountId);
        Assert.Equal(-200_000m, entries[0].Amount);

        // Pos melepas 200rb, sisa 300rb — tetap Dana Pacaran.
        Assert.Equal(300_000m, result.SetAside.Amount);
        Assert.Equal(0m, await _balances.GetActualBalanceAsync(cash.Id));
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(seabank.Id));
    }

    [Fact]
    public async Task Spend_WhenSetAsideEmpty_TakesFullAmountFromSourceAccount()
    {
        var source = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(source.Id, 200_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 0m);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = source.Id,
            Amount = 50_000m,
            Description = "Makan siang warteg",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Saldo pos 0 → seluruh pemakaian dari sumber yang dipilih.
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.NotNull(result.TransactionId);
        var tx = await _db.Transactions.SingleAsync(t => t.Id == result.TransactionId);
        Assert.Equal(TransactionType.Expense, tx.Type);
        Assert.Equal(50_000m, tx.Amount);

        Assert.Equal(150_000m, await _balances.GetActualBalanceAsync(source.Id));
        Assert.Equal(150_000m, await _balances.GetScopeAvailableAsync(_scopeId));

        // Seluruh pemakaian tetap terhitung sebagai "Terpakai".
        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(50_000m, projected!.UsedAmount);
    }

    [Fact]
    public async Task Spend_WholeAmountLeavesSingleSourceAccount()
    {
        // Model baru: SELURUH nominal keluar dari satu Sumber Dana.
        // Pos hanya melepas porsinya sebagai metadata alokasi.
        var source = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(source.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 300_000m, sourceAccountId: source.Id);

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = source.Id,
            Amount = 400_000m,
            Description = "Belanja campuran",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Seluruh 400rb keluar dari SeaBank.
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(source.Id));
        // Pos melepas 300rb; shortfall 100rb ditanggung uang belum dialokasikan.
        Assert.Equal(0m, result.SetAside.Amount);

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(400_000m, projected!.UsedAmount);
        Assert.Equal(0m, projected.Amount);
    }

    [Fact]
    public async Task Spend_SourceBalanceInsufficient_Rejected()
    {
        var source = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(source.Id, 10_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 0m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = source.Id,
                Amount = 50_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));

        Assert.Contains("Insufficient balance", ex.Message);
        Assert.Equal(10_000m, await _balances.GetActualBalanceAsync(source.Id));
    }

    [Fact]
    public async Task Spend_ShortfallExceedsScopeAvailable_Rejected()
    {
        // Uang hanya 100rb, semua sudah dialokasikan ke pos lain.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 100_000m);
        await CreateSetAsideAsync("Pos Lain", 100_000m, sourceAccountId: account.Id);
        var mine = await CreateSetAsideAsync("Dana Makan", 0m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(mine.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = account.Id,
                Amount = 50_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));

        // Saldo aktual account cukup (100rb ≥ 50rb), tapi semua sudah dialokasikan.
        Assert.Contains("Insufficient available money", ex.Message);
    }

    [Fact]
    public async Task Spend_NeverConsumesOtherSetAsides()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var mine = await CreateSetAsideAsync("Dana Makan", 300_000m, sourceAccountId: account.Id);
        await CreateSetAsideAsync("Dana Servis", 200_000m, sourceAccountId: account.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(mine.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = account.Id,
                Amount = 400_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));

        // 400rb − 300rb pos = 100rb shortfall; TotalAvailable = 0 → ditolak.
        Assert.Contains("Insufficient available money", ex.Message);
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(mine.Id));
    }

    [Fact]
    public async Task Spend_ReleasesEverything_WhenSpendingAll()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Kacamata", 400_000m, sourceAccountId: account.Id);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 400_000m,
            Description = "Beli kacamata",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(100_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    // ───────────────────────── Key user scenario: transfer doesn't touch pos ─────────────────────────

    [Fact]
    public async Task Transfer_BetweenAccounts_DoesNotAffectSetAside()
    {
        // Skenario user: Dana Pacaran Rp500.000, tarik Rp200.000 menjadi tunai.
        // Rp200.000 tetap Dana Pacaran — hanya Sumber Dananya berubah.
        var seabank = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(seabank.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Dana Pacaran", 500_000m, sourceAccountId: seabank.Id);

        var tunai = await CreateAccountAsync("Tunai");

        var (tx, _) = await _transactions.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 200_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = seabank.Id, Amount = -200_000m },
                new CreateTransactionEntryCommand { AccountId = tunai.Id, Amount = 200_000m }
            ]
        });

        Assert.NotNull(tx);
        // Pos TIDAK berubah — alokasi mengikuti uang, bukan akun.
        Assert.Equal(500_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(300_000m, await _balances.GetActualBalanceAsync(seabank.Id));
        Assert.Equal(200_000m, await _balances.GetActualBalanceAsync(tunai.Id));
        // TotalAvailable tidak berubah — transfer bukan alokasi.
        Assert.Equal(0m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    // ───────────────────────── Close ─────────────────────────

    [Fact]
    public async Task Close_ReleasesRemaining_AndCreatesNoTransaction()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Tabungan", 400_000m, sourceAccountId: account.Id);
        var txCountBefore = await _db.Transactions.CountAsync();

        var result = await _sut.CloseAsync(setAside.Id, new CloseSetAsideCommand
        {
            ScopeId = _scopeId,
            Reason = SetAsideCloseReason.Cancelled
        });

        Assert.Equal(SetAsideStatus.Closed, result.SetAside.Status);
        Assert.Equal(SetAsideCloseReason.Cancelled, result.SetAside.CloseReason);
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.Equal(1_000_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Closed_SetAside_IsExcludedFromActiveTotals()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Tabungan", 400_000m, sourceAccountId: account.Id);
        await _sut.CloseAsync(setAside.Id, new CloseSetAsideCommand { ScopeId = _scopeId });

        Assert.Equal(0m, await _balances.GetActiveSetAsideTotalAsync(_scopeId));
        Assert.Equal(1_000_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    // ───────────────────────── History ─────────────────────────

    [Fact]
    public async Task History_TracksEveryChange()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 0m);

        await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 300_000m,
            Note = "topup"
        });
        await _sut.WithdrawAsync(setAside.Id, new WithdrawFromSetAsideCommand { ScopeId = _scopeId, Amount = 50_000m, Note = "tarik" });
        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 100_000m,
            Description = "makan",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        var history = await _sut.GetHistoryAsync(setAside.Id, _scopeId);

        Assert.Equal(3, history.Items.Count);
        Assert.Equal(history.Items.Sum(h => h.Amount), await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Contains(history.Items, h => h.Type == SetAsideEntryType.Added && h.Amount == 300_000m);
        Assert.Contains(history.Items, h => h.Type == SetAsideEntryType.Withdrawn && h.Amount == -50_000m);
        Assert.Contains(history.Items, h => h.Type == SetAsideEntryType.Spent && h.Amount == -100_000m && h.TransactionId.HasValue);
        var spendEntry = history.Items.Single(h => h.Type == SetAsideEntryType.Spent);
        Assert.NotNull(spendEntry.Transaction);
        Assert.Equal("makan", spendEntry.Transaction!.Description);
        Assert.Equal(TransactionType.Expense, spendEntry.Transaction.Type);
    }

    [Fact]
    public async Task History_UsesCursorPagesWithoutRepeatingEntries()
    {
        var setAside = await CreateSetAsideAsync("Riwayat", 0m);
        var timestamp = DateTime.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
            _db.SetAsideEntries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = _scopeId,
                Type = SetAsideEntryType.Added,
                Amount = i + 1,
                CreatedAt = timestamp.AddMinutes(i)
            });
        await _db.SaveChangesAsync();

        var first = await _sut.GetHistoryAsync(setAside.Id, _scopeId, pageSize: 2);
        var second = await _sut.GetHistoryAsync(setAside.Id, _scopeId, first.NextCursor, 2);
        var third = await _sut.GetHistoryAsync(setAside.Id, _scopeId, second.NextCursor, 2);

        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal(2, second.Items.Count);
        Assert.True(second.HasMore);
        Assert.Single(third.Items);
        Assert.False(third.HasMore);
        Assert.Equal(5, first.Items.Concat(second.Items).Concat(third.Items).Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task History_CursorPagesKeepHistoricalBalanceAfterAccurate()
    {
        var setAside = await CreateSetAsideAsync("Saldo histori", 0m);
        var timestamp = DateTime.UtcNow.AddMinutes(-10);
        var deltas = new[] { 10m, 20m, -3m, 5m, -2m };
        for (var i = 0; i < deltas.Length; i++)
            _db.SetAsideEntries.Add(new SetAsideEntry
            {
                SetAsideId = setAside.Id,
                ScopeId = _scopeId,
                Type = deltas[i] > 0 ? SetAsideEntryType.Added : SetAsideEntryType.Withdrawn,
                Amount = deltas[i],
                CreatedAt = timestamp.AddMinutes(i)
            });
        await _db.SaveChangesAsync();

        var first = await _sut.GetHistoryAsync(setAside.Id, _scopeId, pageSize: 2);
        var second = await _sut.GetHistoryAsync(setAside.Id, _scopeId, first.NextCursor, 2);
        var third = await _sut.GetHistoryAsync(setAside.Id, _scopeId, second.NextCursor, 2);
        var items = first.Items.Concat(second.Items).Concat(third.Items).ToList();

        Assert.Equal(5, items.Count);
        Assert.Equal(new[] { 30m, 32m, 27m, 30m, 10m }, items.Select(item => item.BalanceAfter));
    }

    [Fact]
    public async Task IncomeAllocatedToCyclingSetAsideFundsShortfall_AndReversalRestoresIt()
    {
        var account = await CreateAccountAsync("Pemasukan");
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Dana Siklus",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 0m
        });

        var (income, _) = await _transactions.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 200_000m,
            OccurredOn = BusinessDate.TodayWib,
            SetAsideId = setAside.Id,
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = 200_000m }]
        });

        var funded = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(200_000m, funded!.Amount);
        Assert.Equal(100_000m, funded.CycleFundingShortfall);
        Assert.Contains((await _sut.GetHistoryAsync(setAside.Id, _scopeId)).Items,
            entry => entry.Type == SetAsideEntryType.CycleFunding && entry.Amount == 200_000m);

        await _transactions.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = income.Id
        });

        var reversed = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(0m, reversed!.Amount);
        Assert.Equal(300_000m, reversed.CycleFundingShortfall);
    }

    [Fact]
    public async Task Expense_RejectsTransactionAmountThatDiffersFromAccountDebit()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Dana Belanja", 100_000m);
        var transactionCount = await _db.Transactions.CountAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _transactions.CreateTransactionAsync(new CreateTransactionCommand
            {
                ScopeId = _scopeId,
                Type = TransactionType.Expense,
                Amount = 200_000m,
                OccurredOn = BusinessDate.TodayWib,
                SetAsideId = setAside.Id,
                Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -100_000m }]
            }));

        Assert.Contains("must equal", exception.Message);
        Assert.Equal(transactionCount, await _db.Transactions.CountAsync());
        Assert.Equal(100_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Create_RejectsUnknownEnumValues()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Jenis invalid",
            Kind = (SetAsideKind)999
        }));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Siklus invalid",
            CycleKind = (SetAsideCycleKind)999
        }));
    }

    [Fact]
    public async Task Close_RejectsUnknownReason()
    {
        var setAside = await CreateSetAsideAsync("Alasan invalid", 0m);
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CloseAsync(setAside.Id,
            new CloseSetAsideCommand { ScopeId = _scopeId, Reason = (SetAsideCloseReason)999 }));
    }

    [Fact]
    public async Task Update_RejectsUnknownCycleEnum()
    {
        var setAside = await CreateSetAsideAsync("Siklus invalid", 0m);
        await Assert.ThrowsAsync<ValidationException>(() => _sut.UpdateSetAsideAsync(setAside.Id,
            new UpdateSetAsideCommand { ScopeId = _scopeId, CycleKind = (SetAsideCycleKind)999 }));
    }

    // ───────────────────────── Scope isolation ─────────────────────────

    [Fact]
    public async Task CrossScope_SetAside_NotAccessible()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Pos A", 100_000m, sourceAccountId: account.Id);

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
    public async Task CrossScope_SourceAccount_RejectedWhenFunding()
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
                SourceAccountId = foreignAccount.Id,
                Name = "Pos",
                Amount = 100_000m
            }));
    }

    // ───────────────────────── Archived account ─────────────────────────

    [Fact]
    public async Task ArchivedSourceAccount_RejectsFunding()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        account.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = account.Id,
                Name = "Pos",
                Amount = 100_000m
            }));
    }

    [Fact]
    public async Task Create_WithoutFunding_AllowedEvenIfOnlyArchivedAccountsExist()
    {
        // Pos independen dari akun — tanpa pendanaan awal, arsip akun tidak relevan.
        var account = await CreateAccountAsync("SeaBank");
        account.IsArchived = true;
        await _db.SaveChangesAsync();

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Pos Baru",
            Amount = 0m
        });

        Assert.NotNull(setAside);
        Assert.Null(setAside.AccountId);
    }

    [Fact]
    public async Task ArchivedSourceAccount_RejectsSpend()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 200_000m, sourceAccountId: account.Id);

        account.IsArchived = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = account.Id,
                Amount = 10_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));
    }

    // ───────────────────────── Cycle: target saldo per siklus ─────────────────────────

    [Fact]
    public async Task RoutineBatch_SpendReleasesUnusedAmountAndCanOnlyExecuteOncePerCycle()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Servis Motor",
            Kind = SetAsideKind.RoutineBatch,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 300_000m
        });

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 250_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(0m, result.SetAside.Amount);
        Assert.Equal(750_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        Assert.True(result.SetAside.IsCycleExecuted);
        Assert.Equal(250_000m, result.SetAside.UsedAmount);
        Assert.Contains((await _sut.GetHistoryAsync(setAside.Id, _scopeId)).Items, entry =>
            entry.Type == SetAsideEntryType.Released
            && entry.Amount == -50_000m
            && entry.TransactionId == result.TransactionId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = account.Id,
                Amount = 10_000m,
                OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
            }));
    }

    [Fact]
    public async Task RoutineBatch_ReversalRestoresAllocationAndReopensCycleExecution()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Servis Motor",
            Kind = SetAsideKind.RoutineBatch,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 300_000m
        });

        var spent = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 250_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        await _transactions.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = spent.TransactionId!.Value
        });

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(300_000m, projected!.Amount);
        Assert.Equal(0m, projected.UsedAmount);
        Assert.False(projected.IsCycleExecuted);
        Assert.Equal(700_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    [Fact]
    public async Task RoutineBatch_ExpenseFromGeneralTransactionAlsoCompletesCycleAndReleasesRemainder()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Servis Motor",
            Kind = SetAsideKind.RoutineBatch,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 300_000m
        });

        await _transactions.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 280_000m,
            // Periode mengikuti tanggal pencatatan, bukan tanggal kejadian transaksi.
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
            SetAsideId = setAside.Id,
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -280_000m }]
        });

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(0m, projected!.Amount);
        Assert.True(projected.IsCycleExecuted);
        Assert.Equal(280_000m, projected.UsedAmount);
        Assert.Equal(720_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    [Fact]
    public async Task AddWithdrawAndClose_NormalizePendingCycleBeforeApplyingMutation()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        async Task<SetAside> CreateUnderfundedCycleAsync(string name)
        {
            var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
                SourceAccountId = account.Id,
                Name = name,
                Kind = SetAsideKind.RoutineIncremental,
                TargetAmount = 300_000m,
                CycleKind = SetAsideCycleKind.Monthly,
                Amount = 100_000m
            });
            await ForceCycleRolloverAsync(setAside.Id);
            return setAside;
        }

        var addTarget = await CreateUnderfundedCycleAsync("Tambah");
        var added = await _sut.AddAsync(addTarget.Id, new AddToSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 10_000m
        });
        Assert.Equal(310_000m, added.SetAside.Amount);
        Assert.Contains((await _sut.GetHistoryAsync(addTarget.Id, _scopeId)).Items, entry =>
            entry.Type == SetAsideEntryType.CycleFunding && entry.Amount == 200_000m);

        var withdrawTarget = await CreateUnderfundedCycleAsync("Tarik");
        var withdrawn = await _sut.WithdrawAsync(withdrawTarget.Id, new WithdrawFromSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 10_000m
        });
        Assert.Equal(290_000m, withdrawn.SetAside.Amount);
        Assert.Contains((await _sut.GetHistoryAsync(withdrawTarget.Id, _scopeId)).Items, entry =>
            entry.Type == SetAsideEntryType.CycleFunding && entry.Amount == 200_000m);

        var closeTarget = await CreateUnderfundedCycleAsync("Tutup");
        var closed = await _sut.CloseAsync(closeTarget.Id, new CloseSetAsideCommand
        {
            ScopeId = _scopeId,
            Reason = SetAsideCloseReason.Cancelled
        });
        Assert.Equal(SetAsideStatus.Closed, closed.SetAside.Status);
        Assert.Equal(0m, closed.SetAside.Amount);
        var closeHistory = (await _sut.GetHistoryAsync(closeTarget.Id, _scopeId)).Items;
        Assert.Contains(closeHistory, entry =>
            entry.Type == SetAsideEntryType.CycleFunding && entry.Amount == 200_000m);
        Assert.Contains(closeHistory, entry =>
            entry.Type == SetAsideEntryType.Closed && entry.Amount == -300_000m);
    }

    [Fact]
    public async Task IncomeAutomaticallyFundsCycleShortfallsInCreationOrder()
    {
        var account = await CreateAccountAsync("BCA Digital");
        var first = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Dana Pertama",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 0m
        });
        var second = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Dana Kedua",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 300_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 0m
        });

        var secondEntity = await _db.SetAsides.SingleAsync(sa => sa.Id == second.Id);
        secondEntity.CreatedAt = first.CreatedAt.AddMinutes(1);
        await _db.SaveChangesAsync();

        await ForceCycleRolloverAsync(first.Id);
        await TouchAsync(first.Id);
        await ForceCycleRolloverAsync(second.Id);
        await TouchAsync(second.Id);
        Assert.Equal(300_000m, (await _sut.GetSetAsideAsync(first.Id, _scopeId))!.CycleFundingShortfall);
        Assert.Equal(300_000m, (await _sut.GetSetAsideAsync(second.Id, _scopeId))!.CycleFundingShortfall);
        Assert.Equal(0m, await _balances.GetScopeFreeCashAsync(_scopeId));

        var income = await _transactions.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 400_000m,
            OccurredOn = BusinessDate.TodayWib,
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = 400_000m }]
        });

        var firstProjected = await _sut.GetSetAsideAsync(first.Id, _scopeId);
        var secondProjected = await _sut.GetSetAsideAsync(second.Id, _scopeId);
        Assert.Equal(300_000m, firstProjected!.Amount);
        Assert.Equal(0m, firstProjected.CycleFundingShortfall);
        Assert.Equal(100_000m, secondProjected!.Amount);
        Assert.Equal(200_000m, secondProjected.CycleFundingShortfall);
        Assert.Equal(0m, await _balances.GetScopeFreeCashAsync(_scopeId));

        await _transactions.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = income.transaction.Id
        });
        Assert.Equal(0m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(300_000m, (await _sut.GetSetAsideAsync(first.Id, _scopeId))!.CycleFundingShortfall);
        Assert.Equal(300_000m, (await _sut.GetSetAsideAsync(second.Id, _scopeId))!.CycleFundingShortfall);
    }

    [Fact]
    public async Task SingleSpend_ClosesAfterExpense_AndReversalReopensIt()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Kulkas",
            Kind = SetAsideKind.SingleSpend,
            TargetAmount = 300_000m,
            Amount = 300_000m
        });

        var result = await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 250_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(SetAsideStatus.Closed, result.SetAside.Status);
        Assert.Equal(SetAsideCloseReason.Spent, result.SetAside.CloseReason);
        Assert.Equal(0m, result.SetAside.Amount);
        Assert.Equal(750_000m, await _balances.GetScopeAvailableAsync(_scopeId));

        await _transactions.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = result.TransactionId!.Value
        });

        var reopened = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(SetAsideStatus.Active, reopened!.Status);
        Assert.Equal(300_000m, reopened.Amount);
        Assert.Equal(700_000m, await _balances.GetScopeAvailableAsync(_scopeId));
    }

    [Fact]
    public async Task CycleReset_Underspend_TopUpOnlyWhatIsMissing()
    {
        // Target 300rb, tersisa 200rb → hanya 100rb yang diambil dari TotalAvailable.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync("Dana Makan", 300_000m, SetAsideCycleKind.Monthly, sourceAccountId: account.Id);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(200_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(700_000m, await _balances.GetScopeAvailableAsync(_scopeId));

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        // Tepat kembali ke target, bukan 200rb + 300rb.
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(600_000m, await _balances.GetScopeAvailableAsync(_scopeId));
        await AssertCycleFundingAsync(setAside.Id, 100_000m);
    }

    [Fact]
    public async Task CycleReset_SpentAll_RefillsToTarget()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync("Dana Makan", 300_000m, SetAsideCycleKind.Monthly, sourceAccountId: account.Id);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
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
        // Plafon 300rb, pengeluaran 400rb → 100rb kelebihan ditanggung uang bebas.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateCyclingSetAsideAsync("Dana Makan", 300_000m, SetAsideCycleKind.Monthly, sourceAccountId: account.Id);

        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 400_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(0m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(600_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(600_000m, await _balances.GetScopeAvailableAsync(_scopeId));

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
        var setAside = await CreateCyclingSetAsideAsync("Dana Makan", 300_000m, SetAsideCycleKind.Monthly, sourceAccountId: account.Id);

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
        var setAside = await CreateCyclingSetAsideAsync("Dana Makan", 300_000m, SetAsideCycleKind.Monthly, sourceAccountId: account.Id);

        // Saldo di atas target, misalnya hasil koreksi.
        await SeedSetAsideEntryAsync(setAside.Id, SetAsideEntryType.Added, 150_000m);
        Assert.Equal(450_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));

        await ForceCycleRolloverAsync(setAside.Id);
        await TouchAsync(setAside.Id);

        // Surplus 150rb dikembalikan ke TotalAvailable, saldo kembali ke target.
        Assert.Equal(300_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        Assert.Equal(700_000m, await _balances.GetScopeAvailableAsync(_scopeId));

        var history = (await _sut.GetHistoryAsync(setAside.Id, _scopeId)).Items;
        Assert.Contains(history, h => h.Type == SetAsideEntryType.Released && h.Amount == -150_000m);
    }

    [Fact]
    public async Task CycleReset_Underfunded_ReportsShortfallInsteadOfInventingMoney()
    {
        // TotalAvailable hanya 100rb, target siklus 300rb.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 100_000m);
        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
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
        Assert.Equal(0m, await _balances.GetScopeAvailableAsync(_scopeId));
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
        await AssertCycleFundingAsync(setAside.Id, 100_000m);

        var projected = await _sut.GetSetAsideAsync(setAside.Id, _scopeId);
        Assert.Equal(200_000m, projected!.TargetShortfall);
    }

    [Fact]
    public async Task CycleReset_MultipleSetAsides_ClaimScopeAvailableInCreationOrder()
    {
        // Beberapa pos dengan cycle pending berbagi pool TotalAvailable yang sama —
        // klaim berurutan creation order, tidak double-claim.
        // Dibuat tanpa pendanaan awal (Amount=0) agar klaim cycle benar-benar
        // bersaing di TotalAvailable scope saat rollover.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 200_000m);

        var first = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Pos A",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 150_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 0m
        });
        await Task.Delay(5); // pastikan CreatedAt berbeda
        var second = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            Name = "Pos B",
            Kind = SetAsideKind.RoutineIncremental,
            TargetAmount = 150_000m,
            CycleKind = SetAsideCycleKind.Monthly,
            Amount = 0m
        });

        await ForceCycleRolloverAsync(first.Id);
        await ForceCycleRolloverAsync(second.Id);
        await TouchAsync(first.Id);
        await TouchAsync(second.Id);

        // TotalAvailable 200rb; Pos A (lebih dulu) klaim 150rb, Pos B sisa 50rb.
        Assert.Equal(150_000m, await _balances.GetSetAsideAmountAsync(first.Id));
        Assert.Equal(50_000m, await _balances.GetSetAsideAmountAsync(second.Id));
        await AssertCycleFundingAsync(first.Id, 150_000m);
        await AssertCycleFundingAsync(second.Id, 50_000m);
    }

    [Fact]
    public async Task CycleKind_None_IsNeverRenormalized()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAside = await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
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
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateSetAsideAsync(new CreateSetAsideCommand
            {
                ScopeId = _scopeId,
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
            SourceAccountId = account.Id,
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

    // ───────────────────────── Legacy AccountId tidak dipakai ─────────────────────────

    [Fact]
    public async Task LegacyAccountId_NeverUsedForCalculations()
    {
        // SetAsides.AccountId legacy diisi langsung di DB (data lama).
        // Nilai ini TIDAK BOLEH memengaruhi saldo, available, funding, maupun alokasi.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var other = await CreateAccountAsync("Mandiri");
        await SeedBalanceAsync(other.Id, 500_000m);

        var setAside = await CreateSetAsideAsync("Pos Legacy", 300_000m, sourceAccountId: account.Id);

        // Suntikkan nilai legacy AccountId (mis. data lama menunjuk Mandiri).
        setAside.AccountId = other.Id;
        await _db.SaveChangesAsync();

        var availableBefore = await _balances.GetScopeAvailableAsync(_scopeId);
        // TotalAvailable scope-wide = TotalActual(1.5M) − TotalSetAside(300k) = 1.2M.
        // Legacy AccountId tidak memengaruhi perhitungan ini.
        Assert.Equal(1_200_000m, availableBefore);

        // Top-up tervalidasi terhadap TotalAvailable scope — legacy AccountId diabaikan.
        await _sut.AddAsync(setAside.Id, new AddToSetAsideCommand
        {
            ScopeId = _scopeId,
            Amount = 200_000m
        });

        Assert.Equal(500_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        // TotalAvailable = 1.5M − 500k = 1M.
        Assert.Equal(1_000_000m, await _balances.GetScopeAvailableAsync(_scopeId));

        // Saldo aktual kedua akun tidak dipengaruhi legacy AccountId.
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(500_000m, await _balances.GetActualBalanceAsync(other.Id));

        // Spend memakai SourceAccountId eksplisit — bukan legacy AccountId.
        await _sut.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Amount = 300_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        // Uang keluar dari SeaBank (SourceAccountId), BUKAN dari Mandiri (legacy).
        Assert.Equal(700_000m, await _balances.GetActualBalanceAsync(account.Id));
        Assert.Equal(500_000m, await _balances.GetActualBalanceAsync(other.Id));
        // Pos melepas min(300k, 500k) = 300k → sisa pos 200k.
        Assert.Equal(200_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
    }

    // ───────────────────────── Concurrency ─────────────────────────

    [Fact]
    public async Task Concurrent_Withdrawals_CannotOverdraw()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 100_000m, sourceAccountId: account.Id);

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
                Name = "Pos " + Guid.NewGuid(),
                Amount = 40_000m
            });
        });

        Assert.True(succeeded <= 2, $"More set-asides succeeded than the available balance allows: {succeeded}");
        Assert.True(await _balances.GetScopeAvailableAsync(_scopeId) >= 0m);
        Assert.Equal(100_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Concurrent_SpendAndWithdraw_KeepSetAsideNonNegative()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 500_000m);
        var setAside = await CreateSetAsideAsync("Dana Makan", 100_000m, sourceAccountId: account.Id);

        var spend = Task.Run(async () =>
        {
            using var services = CreateServices();
            try
            {
                await services.SetAsides.SpendAsync(setAside.Id, new SpendFromSetAsideCommand
                {
                    ScopeId = _scopeId,
                    SourceAccountId = account.Id,
                    Amount = 80_000m,
                    OccurredOn = BusinessDate.TodayWib
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
        var spendSucceeded = await spend;

        // Invariant: tidak ada saldo yang pernah menjadi negatif, dan setiap operasi
        // yang sukses benar-benar menggerakkan uang sesuai aturannya.
        var reserved = await _balances.GetSetAsideAmountAsync(setAside.Id);
        var available = await _balances.GetScopeAvailableAsync(_scopeId);
        var actual = await _balances.GetActualBalanceAsync(account.Id);

        Assert.True(reserved >= 0m, $"Set-aside went negative: {reserved}");
        Assert.True(actual >= 0m, $"Actual balance went negative: {actual}");
        Assert.Equal(actual - reserved, available);

        // Spend menggerakkan uang riil; withdraw tidak.
        Assert.Equal(spendSucceeded ? 420_000m : 500_000m, actual);
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
        string name, decimal amount, Guid? sourceAccountId = null)
        => await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = sourceAccountId,
            Name = name,
            Kind = SetAsideKind.Saving,
            Amount = amount
        });

    private async Task<SetAside> CreateCyclingSetAsideAsync(
        string name, decimal target, SetAsideCycleKind cycle, Guid? sourceAccountId = null)
        => await _sut.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = sourceAccountId,
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
        var funding = history.Items.Where(h => h.Type == SetAsideEntryType.CycleFunding).ToList();
        Assert.Equal(expected, funding.Sum(h => h.Amount));
    }

    private async Task AssertNoCycleFundingAsync(Guid setAsideId)
    {
        var history = await _sut.GetHistoryAsync(setAsideId, _scopeId);
        Assert.DoesNotContain(history.Items, h => h.Type == SetAsideEntryType.CycleFunding);
    }
}
