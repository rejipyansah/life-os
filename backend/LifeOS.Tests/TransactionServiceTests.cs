using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class TransactionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly TransactionService _sut;
    private readonly Guid _scopeId;
    private readonly Guid _accountAId;
    private readonly Guid _accountBId;

    public TransactionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new TransactionService(_db);

        // Seed: Scope + two Accounts
        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        _db.SaveChanges();
        _scopeId = scope.Id;

        var accountA = new Account
        {
            ScopeId = _scopeId,
            Name = "Cash Wallet",
            Type = AccountType.Cash
        };
        var accountB = new Account
        {
            ScopeId = _scopeId,
            Name = "Bank Account",
            Type = AccountType.Bank
        };
        _db.Accounts.AddRange(accountA, accountB);
        _db.SaveChanges();
        _accountAId = accountA.Id;
        _accountBId = accountB.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ───────────────────────── Income ─────────────────────────

    [Fact]
    public async Task Income_PositiveAmount_IncreasesBalance()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 100_000m }
            ]
        };

        var (tx, entries) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(TransactionType.Income, tx.Type);
        Assert.Equal(100_000m, tx.Amount);
        Assert.Single(entries);
        Assert.Equal(100_000m, entries[0].Amount);

        var balance = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(100_000m, balance);
    }

    [Fact]
    public async Task Income_ZeroAmount_Rejected()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 0,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 0 }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Income_NegativeAmount_Rejected()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = -100m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -100m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Income_WithFee_Rejected()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 100m,
            FeeAmount = 10m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 100m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Expense ─────────────────────────

    [Fact]
    public async Task Expense_SufficientBalance_Succeeds()
    {
        // Seed: account has 500k
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 18_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -18_000m }
            ]
        };

        var (tx, _) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(TransactionType.Expense, tx.Type);
        var balance = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(482_000m, balance);
    }

    [Fact]
    public async Task Expense_InsufficientBalance_Rejected()
    {
        // Seed: account has only 10k
        await SeedBalance(_accountAId, 10_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 18_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -18_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));

        // Balance must remain unchanged
        var balance = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(10_000m, balance);
    }

    [Fact]
    public async Task Expense_PositiveEntry_Rejected()
    {
        await SeedBalance(_accountAId, 100_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 18_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 18_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Transfer ─────────────────────────

    [Fact]
    public async Task Transfer_WithoutFee_CorrectEffects()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 500_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -500_000m },
                new CreateTransactionEntryCommand { AccountId = _accountBId, Amount = 500_000m }
            ]
        };

        var (tx, entries) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(2, entries.Count);
        Assert.Null(tx.FeeAmount);

        var balanceA = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        var balanceB = await _sut.GetAccountBalanceAsync(_accountBId, _scopeId);

        Assert.Equal(0m, balanceA);
        Assert.Equal(500_000m, balanceB);
    }

    [Fact]
    public async Task Transfer_WithFee_SourceDecreasesCorrectly()
    {
        await SeedBalance(_accountAId, 600_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 500_000m,
            FeeAmount = 2_500m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -502_500m },
                new CreateTransactionEntryCommand { AccountId = _accountBId, Amount = 500_000m }
            ]
        };

        var (tx, _) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(2_500m, tx.FeeAmount);

        var balanceA = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        var balanceB = await _sut.GetAccountBalanceAsync(_accountBId, _scopeId);

        Assert.Equal(97_500m, balanceA);
        Assert.Equal(500_000m, balanceB);
    }

    [Fact]
    public async Task Transfer_SameSourceDestination_Rejected()
    {
        await SeedBalance(_accountAId, 100_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -100_000m },
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 100_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Transfer_NegativeFee_Rejected()
    {
        await SeedBalance(_accountAId, 100_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 100_000m,
            FeeAmount = -100m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -100_000m },
                new CreateTransactionEntryCommand { AccountId = _accountBId, Amount = 100_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Transfer_InsufficientBalance_Rejected()
    {
        await SeedBalance(_accountAId, 10_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 50_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -50_000m },
                new CreateTransactionEntryCommand { AccountId = _accountBId, Amount = 50_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Refund ─────────────────────────

    [Fact]
    public async Task Refund_ExpenseEntry_CreatesPositiveEntry()
    {
        // Seed: expense transaction
        var expenseTx = new Transaction
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 50_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(expenseTx);
        await _db.SaveChangesAsync();

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Refund,
            Amount = 50_000m,
            RelatedTransactionId = expenseTx.Id,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 50_000m }
            ]
        };

        var (tx, entries) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(TransactionType.Refund, tx.Type);
        Assert.Equal(expenseTx.Id, tx.RelatedTransactionId);
        Assert.Single(entries);
        Assert.Equal(50_000m, entries[0].Amount);
    }

    [Fact]
    public async Task Refund_NonExpense_Rejected()
    {
        var incomeTx = new Transaction
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 50_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(incomeTx);
        await _db.SaveChangesAsync();

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Refund,
            Amount = 50_000m,
            RelatedTransactionId = incomeTx.Id,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 50_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Refund_MissingRelatedTransaction_Rejected()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Refund,
            Amount = 50_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 50_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Reversal ─────────────────────────

    [Fact]
    public async Task Reversal_RequiredFieldsValidated()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Reversal,
            Amount = 10_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -10_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Reversal_SameScopeRelationRequired()
    {
        // Create a transaction in a different scope
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherTx = new Transaction
        {
            ScopeId = otherScope.Id,
            Type = TransactionType.Expense,
            Amount = 10_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(otherTx);
        await _db.SaveChangesAsync();

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Reversal,
            Amount = 10_000m,
            RelatedTransactionId = otherTx.Id,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 10_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task Reversal_ZeroAmount_Rejected()
    {
        var relatedTx = new Transaction
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 10_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(relatedTx);
        await _db.SaveChangesAsync();

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Reversal,
            Amount = 10_000m,
            RelatedTransactionId = relatedTx.Id,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 0m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Adjustment ─────────────────────────

    [Fact]
    public async Task Adjustment_PositiveCorrection_Succeeds()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Adjustment,
            Amount = 5_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 5_000m }
            ]
        };

        var (tx, entries) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(TransactionType.Adjustment, tx.Type);
        Assert.Single(entries);
        Assert.Equal(5_000m, entries[0].Amount);
    }

    [Fact]
    public async Task Adjustment_NegativeCorrection_Succeeds()
    {
        await SeedBalance(_accountAId, 100_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Adjustment,
            Amount = 5_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -5_000m }
            ]
        };

        var (tx, entries) = await _sut.CreateTransactionAsync(command);

        Assert.Equal(TransactionType.Adjustment, tx.Type);
        Assert.Equal(-5_000m, entries[0].Amount);

        var balance = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(95_000m, balance);
    }

    [Fact]
    public async Task Adjustment_ZeroAmount_Rejected()
    {
        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Adjustment,
            Amount = 0m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = 0m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Scope isolation ─────────────────────────

    [Fact]
    public async Task Transaction_OutOfScopeAccount_Rejected()
    {
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherAccount = new Account
        {
            ScopeId = otherScope.Id,
            Name = "Foreign Account",
            Type = AccountType.Bank
        };
        _db.Accounts.Add(otherAccount);
        await _db.SaveChangesAsync();

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 10_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = otherAccount.Id, Amount = 10_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));
    }

    // ───────────────────────── Atomicity ─────────────────────────

    [Fact]
    public async Task Atomicity_ValidationFailure_NoPartialData()
    {
        // Attempt an expense with insufficient balance
        await SeedBalance(_accountAId, 1_000m);

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 50_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -50_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateTransactionAsync(command));

        // Verify no Expense Transaction or Entry was created
        Assert.Empty(_db.Transactions.Where(t => t.ScopeId == _scopeId && t.Type == TransactionType.Expense));
        // Verify no entries exist for the expense (seed entries belong to Income)
        var seedTx = _db.Transactions.First(t => t.ScopeId == _scopeId && t.Type == TransactionType.Income);
        Assert.Empty(_db.TransactionEntries.Where(te => te.TransactionId != seedTx.Id));
    }

    // ───────────────────────── Immutability ─────────────────────────

    [Fact]
    public async Task Immutability_NoUpdatePath_Exposed()
    {
        // This test documents that TransactionService does not expose
        // any update/mutation methods for posted transactions.
        // The only public methods are CreateTransactionAsync and GetAccountBalanceAsync.

        var methodNames = typeof(TransactionService)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain(methodNames, m =>
            m.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    // ───────────────────────── Concurrency ─────────────────────────

    [Fact]
    public async Task Concurrency_SerializablePreventsDoubleSpend()
    {
        await SeedBalance(_accountAId, 100_000m);

        // Each task creates its own DbContext to simulate separate HTTP requests
        var tasks = new List<Task<bool>>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(_connection)
                    .Options;

                using var db = new ApplicationDbContext(options);
                var service = new TransactionService(db);
                try
                {
                    await service.CreateTransactionAsync(new CreateTransactionCommand
                    {
                        ScopeId = _scopeId,
                        Type = TransactionType.Expense,
                        Amount = 50_000m,
                        OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
                        Entries =
                        [
                            new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -50_000m }
                        ]
                    });
                    return true;
                }
                catch
                {
                    return false;
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        var succeeded = results.Count(r => r);

        // With 100k balance and 50k spends, at most 2 can succeed
        Assert.True(succeeded <= 2, $"Expected at most 2 successes, got {succeeded}");

        var balance = await _sut.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.True(balance >= 0, $"Balance should not be negative: {balance}");
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task SeedBalance(Guid accountId, decimal amount)
    {
        if (amount == 0) return;

        var sign = amount > 0 ? 1 : -1;
        var absAmount = Math.Abs(amount);

        var tx = new Transaction
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = absAmount,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };

        var entry = new TransactionEntry
        {
            AccountId = accountId,
            Amount = amount
        };

        _db.Transactions.Add(tx);
        entry.TransactionId = tx.Id;
        _db.TransactionEntries.Add(entry);
        await _db.SaveChangesAsync();
    }
}
