using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class TransactionReversalTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly TransactionService _sut;
    private readonly AccountService _accounts;
    private readonly BalanceCalculator _balances;
    private readonly Guid _scopeId;

    public TransactionReversalTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _balances = new BalanceCalculator(_db);
        _sut = new TransactionService(_db);
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

    [Fact]
    public async Task Reverse_RestoresBalance_AndKeepsOriginalIntact()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var (expense, _) = await _sut.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 250_000m,
            Description = "Salah catat",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -250_000m }]
        });

        Assert.Equal(750_000m, await _balances.GetActualBalanceAsync(account.Id));

        var reversal = await _sut.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = expense.Id,
            Reason = "Salah catat / nominal typo"
        });

        // Saldo kembali utuh.
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));

        // Transaksi asli tidak diubah sama sekali — audit trail utuh.
        var original = await _db.Transactions.AsNoTracking().SingleAsync(t => t.Id == expense.Id);
        Assert.Equal(TransactionType.Expense, original.Type);
        Assert.Equal(250_000m, original.Amount);
        Assert.Equal("Salah catat", original.Description);

        // Reversal terhubung ke transaksi asli.
        Assert.Equal(TransactionType.Reversal, reversal.Type);
        Assert.Equal(expense.Id, reversal.RelatedTransactionId);
        Assert.Equal("Salah catat / nominal typo", reversal.Description);

        var entries = await _db.TransactionEntries
            .Where(te => te.TransactionId == reversal.Id)
            .ToListAsync();
        Assert.Single(entries);
        Assert.Equal(250_000m, entries[0].Amount);
    }

    [Fact]
    public async Task Reverse_PreservesAuditHistory_OnReadModel()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);
        var txCountBefore = await _db.Transactions.CountAsync();

        var (expense, _) = await _sut.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 250_000m,
            Description = "Salah catat",
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -250_000m }]
        });

        await _sut.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = expense.Id,
            Reason = "Duplikat transaksi"
        });

        var items = await _sut.GetTransactionsAsync(_scopeId);
        var original = items.Single(t => t.Id == expense.Id);

        Assert.True(original.IsReversed);
        Assert.Equal("Duplikat transaksi", original.ReversalReason);

        // Kedua baris tetap bisa ditelusuri (seed + expense + reversal).
        Assert.Equal(txCountBefore + 2, items.Count);
    }

    [Fact]
    public async Task Reverse_Twice_IsRejected()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var (expense, _) = await _sut.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -100_000m }]
        });

        await _sut.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = expense.Id
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.ReverseTransactionAsync(new ReverseTransactionCommand
            {
                ScopeId = _scopeId,
                TransactionId = expense.Id
            }));

        Assert.Contains("already been reversed", ex.Message);
    }

    [Fact]
    public async Task Reverse_Transfer_RestoresBothAccounts()
    {
        var source = await CreateAccountAsync("BCA");
        var destination = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(source.Id, 1_000_000m);

        var (transfer, _) = await _sut.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 300_000m,
            FeeAmount = 2_500m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = source.Id, Amount = -302_500m },
                new CreateTransactionEntryCommand { AccountId = destination.Id, Amount = 300_000m }
            ]
        });

        Assert.Equal(697_500m, await _balances.GetActualBalanceAsync(source.Id));
        Assert.Equal(300_000m, await _balances.GetActualBalanceAsync(destination.Id));

        await _sut.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = transfer.Id,
            Reason = "Transfer salah tujuan"
        });

        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(source.Id));
        Assert.Equal(0m, await _balances.GetActualBalanceAsync(destination.Id));
    }

    [Fact]
    public async Task Reverse_CrossScope_IsRejected()
    {
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var (expense, _) = await _sut.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -100_000m }]
        });

        var otherScope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.ReverseTransactionAsync(new ReverseTransactionCommand
            {
                ScopeId = otherScope.Id,
                TransactionId = expense.Id
            }));

        Assert.Equal(900_000m, await _balances.GetActualBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Reverse_PreservesSetAsideSemantics()
    {
        // Membatalkan transaksi tidak boleh mengubah uang yang disisihkan.
        var account = await CreateAccountAsync("SeaBank");
        await SeedBalanceAsync(account.Id, 1_000_000m);

        var setAsides = new SetAsideService(_db, _balances);
        var setAside = await setAsides.CreateSetAsideAsync(new CreateSetAsideCommand
        {
            ScopeId = _scopeId,
            SourceAccountId = account.Id,
            Name = "Tabungan",
            Amount = 400_000m
        });

        var (expense, _) = await _sut.CreateTransactionAsync(new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 200_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = account.Id, Amount = -200_000m }]
        });

        await _sut.ReverseTransactionAsync(new ReverseTransactionCommand
        {
            ScopeId = _scopeId,
            TransactionId = expense.Id
        });

        // Saldo aktual kembali utuh setelah reversal.
        Assert.Equal(1_000_000m, await _balances.GetActualBalanceAsync(account.Id));
        // Set-aside tidak terpengaruh transaksi biasa (tanpa SetAsideId).
        Assert.Equal(400_000m, await _balances.GetSetAsideAmountAsync(setAside.Id));
        // TotalAvailable scope-wide = TotalActual − TotalSetAside = 1M − 400k.
        Assert.Equal(600_000m, await _balances.GetScopeAvailableAsync(_scopeId));
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
