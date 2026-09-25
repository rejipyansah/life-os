using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class AllocationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly AllocationService _sut;
    private readonly TransactionService _txService;
    private readonly Guid _scopeId;
    private readonly Guid _accountAId;
    private readonly Guid _accountBId;

    public AllocationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new AllocationService(_db);
        _txService = new TransactionService(_db);

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

    // ───────────────────────── Creation ─────────────────────────

    [Fact]
    public async Task Create_ValidAllocation_Succeeds()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Vacation Fund",
            Amount = 500_000m
        };

        var allocation = await _sut.CreateAllocationAsync(command);

        Assert.Equal(_accountAId, allocation.AccountId);
        Assert.Equal("Vacation Fund", allocation.Name);
        Assert.Equal(500_000m, allocation.Amount);
        Assert.Equal(AllocationStatus.Active, allocation.Status);
        Assert.Equal(_scopeId, allocation.ScopeId);
    }

    [Fact]
    public async Task Create_ZeroAmount_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Zero Fund",
            Amount = 0
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    [Fact]
    public async Task Create_NegativeAmount_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Negative Fund",
            Amount = -100m
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    [Fact]
    public async Task Create_EmptyName_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "",
            Amount = 100m
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    [Fact]
    public async Task Create_WhitespaceName_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "   ",
            Amount = 100m
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    [Fact]
    public async Task Create_NonExistentAccount_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = Guid.NewGuid(),
            Name = "Ghost Fund",
            Amount = 100m
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    [Fact]
    public async Task Create_ArchivedAccount_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);
        await ArchiveAccount(_accountAId);

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Archived Fund",
            Amount = 100m
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────── Create: Balance Validation ─────────────────────────

    [Fact]
    public async Task Create_WithinAvailableBalance_Succeeds()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 500_000m
        });

        Assert.Equal(500_000m, allocation.Amount);
    }

    [Fact]
    public async Task Create_ExceedingAvailableBalance_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAllocationAsync(new CreateAllocationCommand
            {
                ScopeId = _scopeId,
                AccountId = _accountAId,
                Name = "Oversized Fund",
                Amount = 600_000m
            }));
        Assert.Contains("tidak mencukupi", ex.Message);
    }

    [Fact]
    public async Task Create_MultipleOnSameAccount_StacksWithinBalance()
    {
        await SeedBalance(_accountAId, 500_000m);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund 1",
            Amount = 200_000m
        });

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund 2",
            Amount = 300_000m
        });

        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(500_000m, allocated);
    }

    [Fact]
    public async Task Create_ThirdAllocationExceedingBalance_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund 1",
            Amount = 300_000m
        });

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund 2",
            Amount = 200_000m
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAllocationAsync(new CreateAllocationCommand
            {
                ScopeId = _scopeId,
                AccountId = _accountAId,
                Name = "Fund 3",
                Amount = 1m
            }));
        Assert.Contains("tidak mencukupi", ex.Message);
    }

    [Fact]
    public async Task Create_CompletedAllocationDoesNotCountTowardBalance()
    {
        await SeedBalance(_accountAId, 500_000m);

        var a1 = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund 1",
            Amount = 400_000m
        });

        await _sut.CompleteAllocationAsync(a1.Id, _scopeId);

        // After completion: balance = 100k (500k - 400k expense), no active allocations
        var a2 = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund 2",
            Amount = 100_000m
        });

        Assert.Equal(100_000m, a2.Amount);
    }

    // ───────────────────────── Scope Isolation ─────────────────────────

    [Fact]
    public async Task Create_CrossScopeAccount_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherAccount = new Account
        {
            ScopeId = otherScope.Id,
            Name = "Guest Account",
            Type = AccountType.Cash
        };
        _db.Accounts.Add(otherAccount);
        await _db.SaveChangesAsync();

        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = otherAccount.Id,
            Name = "Cross Scope Fund",
            Amount = 100m
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    [Fact]
    public async Task GetAllocations_ReturnsOnlyCurrentScope()
    {
        await SeedBalance(_accountAId, 500_000m);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Our Fund",
            Amount = 100m
        });

        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherAccount = new Account
        {
            ScopeId = otherScope.Id,
            Name = "Guest Account",
            Type = AccountType.Cash
        };
        _db.Accounts.Add(otherAccount);
        await _db.SaveChangesAsync();

        await SeedBalance(otherAccount.Id, 500_000m, otherScope.Id);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = otherScope.Id,
            AccountId = otherAccount.Id,
            Name = "Other Fund",
            Amount = 200m
        });

        var allocations = await _sut.GetAllocationsAsync(_scopeId);

        Assert.Single(allocations);
        Assert.Equal("Our Fund", allocations[0].Name);
    }

    // ───────────────────────── Edit: Balance Validation ─────────────────────────

    [Fact]
    public async Task Edit_AmountIncrease_WithinBalance_Succeeds()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 200_000m
        });

        var updated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 500_000m
        });

        Assert.Equal(500_000m, updated.Amount);
    }

    [Fact]
    public async Task Edit_AmountIncrease_ExceedingBalance_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 200_000m
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
            {
                ScopeId = _scopeId,
                Amount = 600_000m
            }));
        Assert.Contains("tidak mencukupi", ex.Message);
    }

    [Fact]
    public async Task Edit_NameAndAmount_Succeeds()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        var updated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Name = "WiFi Premium",
            Amount = 500_000m
        });

        Assert.Equal("WiFi Premium", updated.Name);
        Assert.Equal(500_000m, updated.Amount);
        Assert.Equal(AllocationStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Edit_AccountChange_Succeeds()
    {
        await SeedBalance(_accountAId, 500_000m);
        await SeedBalance(_accountBId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100_000m
        });

        var updated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountBId
        });

        Assert.Equal(_accountBId, updated.AccountId);
    }

    [Fact]
    public async Task Edit_AccountChange_ExceedingNewBalance_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100_000m
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
            {
                ScopeId = _scopeId,
                AccountId = _accountBId
            }));
        Assert.Contains("tidak mencukupi", ex.Message);
    }

    [Fact]
    public async Task Edit_AccountChange_ReflectsInAllocated()
    {
        await SeedBalance(_accountAId, 500_000m);
        await SeedBalance(_accountBId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100_000m
        });

        Assert.Equal(100_000m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));
        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId));

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountBId
        });

        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));
        Assert.Equal(100_000m, await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId));
    }

    [Fact]
    public async Task Edit_DoesNotChangeAccountBalance()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var balanceBefore = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 500_000m
        });

        var balanceAfter = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(balanceBefore, balanceAfter);
    }

    [Fact]
    public async Task Edit_ArchivedAccount_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);
        await SeedBalance(_accountBId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100_000m
        });

        // Archive the TARGET account
        await ArchiveAccount(_accountBId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
            {
                ScopeId = _scopeId,
                AccountId = _accountBId
            }));
        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────── Lifecycle: Complete ─────────────────────────

    [Fact]
    public async Task Complete_Allocation_SetsCompleted()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        var completed = await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        Assert.Equal(AllocationStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Complete_CreatesExactlyOneExpense()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        var txCountBefore = await _db.Transactions.CountAsync();
        var entryCountBefore = await _db.TransactionEntries.CountAsync();

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var txCountAfter = await _db.Transactions.CountAsync();
        var entryCountAfter = await _db.TransactionEntries.CountAsync();

        Assert.Equal(txCountBefore + 1, txCountAfter);
        Assert.Equal(entryCountBefore + 1, entryCountAfter);

        var expense = await _db.Transactions
            .Where(t => t.Type == TransactionType.Expense && t.ScopeId == _scopeId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();

        Assert.Equal(350_000m, expense.Amount);
        Assert.Contains("WiFi", expense.Description);

        var entry = await _db.TransactionEntries
            .Where(te => te.TransactionId == expense.Id)
            .FirstAsync();

        Assert.Equal(_accountAId, entry.AccountId);
        Assert.Equal(-350_000m, entry.Amount);
    }

    [Fact]
    public async Task Complete_AlreadyCompleted_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CompleteAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("selesai", ex.Message);
    }

    [Fact]
    public async Task Complete_AlreadyCancelled_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CompleteAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("dibatalkan", ex.Message);
    }

    [Fact]
    public async Task Complete_InsufficientBalance_Rejected()
    {
        // Create allocation with enough balance, then spend most of it
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        // Spend 450k of the 500k balance (only 50k remaining)
        var spendTx = new Transaction
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 450_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(spendTx);
        await _db.SaveChangesAsync();

        _db.TransactionEntries.Add(new TransactionEntry
        {
            TransactionId = spendTx.Id,
            AccountId = _accountAId,
            Amount = -450_000m
        });
        await _db.SaveChangesAsync();

        // Balance is now 50k, allocation is 350k - cannot complete
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CompleteAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("Insufficient balance", ex.Message);
    }

    [Fact]
    public async Task Complete_ExcludedFromActiveTotals()
    {
        await SeedBalance(_accountAId, 1_000_000m);

        var a1 = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Servis Motor",
            Amount = 500_000m
        });

        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(850_000m, allocated);

        await _sut.CompleteAllocationAsync(a1.Id, _scopeId);

        allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(500_000m, allocated);
    }

    [Fact]
    public async Task Complete_RemainsInHistory()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var all = await _sut.GetAllocationsAsync(_scopeId);
        Assert.Single(all);
        Assert.Equal("WiFi", all[0].Name);
        Assert.Equal(AllocationStatus.Completed, all[0].Status);
    }

    [Fact]
    public async Task Complete_DeductsFromBalance()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var balanceBefore = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var balanceAfter = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(balanceBefore - 300_000m, balanceAfter);
    }

    [Fact]
    public async Task Complete_RestoresAvailableAmount()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var available1 = 500_000m - await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(200_000m, available1);

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        // After completion: balance is 200_000 (500k - 300k expense), no active allocations
        var balance = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(200_000m, balance);

        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(0m, allocated);
    }

    [Fact]
    public async Task Complete_NoDuplicateExpenseIfCalledTwice()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var txCountAfterFirst = await _db.Transactions.CountAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CompleteAllocationAsync(allocation.Id, _scopeId));

        var txCountAfterSecond = await _db.Transactions.CountAsync();
        Assert.Equal(txCountAfterFirst, txCountAfterSecond);
    }

    // ───────────────────────── Lifecycle: Cancel ─────────────────────────

    [Fact]
    public async Task Cancel_Allocation_SetsCancelled()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        var cancelled = await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        Assert.Equal(AllocationStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Cancel_CreatesNoExpense()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        var txCountBefore = await _db.Transactions.CountAsync();
        var entryCountBefore = await _db.TransactionEntries.CountAsync();

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var txCountAfter = await _db.Transactions.CountAsync();
        var entryCountAfter = await _db.TransactionEntries.CountAsync();

        Assert.Equal(txCountBefore, txCountAfter);
        Assert.Equal(entryCountBefore, entryCountAfter);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CancelAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("dibatalkan", ex.Message);
    }

    [Fact]
    public async Task Cancel_AlreadyCompleted_Rejected()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CancelAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("selesai", ex.Message);
    }

    [Fact]
    public async Task Cancel_ExcludedFromActiveTotals()
    {
        await SeedBalance(_accountAId, 1_000_000m);

        var a1 = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Servis Motor",
            Amount = 500_000m
        });

        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(850_000m, allocated);

        await _sut.CancelAllocationAsync(a1.Id, _scopeId);

        allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(500_000m, allocated);
    }

    [Fact]
    public async Task Cancel_RemainsInHistory()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var all = await _sut.GetAllocationsAsync(_scopeId);
        Assert.Single(all);
        Assert.Equal("WiFi", all[0].Name);
        Assert.Equal(AllocationStatus.Cancelled, all[0].Status);
    }

    [Fact]
    public async Task Cancel_RestoresAvailableAmount()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var available1 = 500_000m - await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(200_000m, available1);

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var available2 = 500_000m - await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(500_000m, available2);
    }

    [Fact]
    public async Task Cancel_NoTransactionEntryCreated()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        var entryCountBefore = await _db.TransactionEntries.CountAsync();

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var entryCountAfter = await _db.TransactionEntries.CountAsync();
        Assert.Equal(entryCountBefore, entryCountAfter);
    }

    // ───────────────────────── Terminal State Guards ─────────────────────────

    [Fact]
    public async Task CompletedAllocation_CannotBeCompletedAgain()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CompleteAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("selesai", ex.Message);
    }

    [Fact]
    public async Task CompletedAllocation_CannotBeCancelled()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CancelAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("selesai", ex.Message);
    }

    [Fact]
    public async Task CancelledAllocation_CannotBeCompletedAgain()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CompleteAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("dibatalkan", ex.Message);
    }

    [Fact]
    public async Task CancelledAllocation_CannotBeCancelledAgain()
    {
        await SeedBalance(_accountAId, 500_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CancelAllocationAsync(allocation.Id, _scopeId));
        Assert.Contains("dibatalkan", ex.Message);
    }

    // ───────────────────────── Archived Account Guard ─────────────────────────

    [Fact]
    public async Task ArchivedAccount_CannotHaveActiveAllocation()
    {
        await SeedBalance(_accountAId, 500_000m);
        await ArchiveAccount(_accountAId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAllocationAsync(new CreateAllocationCommand
            {
                ScopeId = _scopeId,
                AccountId = _accountAId,
                Name = "Fund",
                Amount = 100m
            }));
        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────── Expense Cannot Consume Allocated Funds ─────────────────────────

    [Fact]
    public async Task Expense_CannotConsumeAllocatedFunds()
    {
        await SeedBalance(_accountAId, 500_000m);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var txCommand = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 400_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -400_000m }]
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _txService.CreateTransactionAsync(txCommand));
        Assert.Contains("available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expense_WithinAvailableBalance_Succeeds()
    {
        await SeedBalance(_accountAId, 500_000m);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var txCommand = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Expense,
            Amount = 200_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries = [new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -200_000m }]
        };

        var (tx, _) = await _txService.CreateTransactionAsync(txCommand);
        Assert.NotNull(tx);
    }

    [Fact]
    public async Task Transfer_SourceCannotConsumeAllocatedFunds()
    {
        await SeedBalance(_accountAId, 500_000m);
        await SeedBalance(_accountBId, 0m);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var txCommand = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 400_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = _accountAId, Amount = -400_000m },
                new CreateTransactionEntryCommand { AccountId = _accountBId, Amount = 400_000m }
            ]
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _txService.CreateTransactionAsync(txCommand));
        Assert.Contains("available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────── Full Lifecycle ─────────────────────────

    [Fact]
    public async Task FullLifecycle_Create_Edit_Complete()
    {
        await SeedBalance(_accountAId, 1_000_000m);
        await SeedBalance(_accountBId, 1_000_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });
        Assert.Equal(AllocationStatus.Active, allocation.Status);
        Assert.Equal(350_000m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 500_000m
        });
        Assert.Equal(500_000m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountBId
        });
        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));
        Assert.Equal(500_000m, await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId));

        await _sut.CompleteAllocationAsync(allocation.Id, _scopeId);
        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId));

        var all = await _sut.GetAllocationsAsync(_scopeId);
        Assert.Single(all);
        Assert.Equal(AllocationStatus.Completed, all[0].Status);
        Assert.Equal("WiFi", all[0].Name);

        var balance = await _txService.GetAccountBalanceAsync(_accountBId, _scopeId);
        Assert.Equal(500_000m, balance);
    }

    [Fact]
    public async Task FullLifecycle_Create_Edit_Cancel()
    {
        await SeedBalance(_accountAId, 1_000_000m);

        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Servis Motor",
            Amount = 200_000m
        });
        Assert.Equal(AllocationStatus.Active, allocation.Status);

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 250_000m
        });
        Assert.Equal(250_000m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));

        await _sut.CancelAllocationAsync(allocation.Id, _scopeId);
        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));

        var all = await _sut.GetAllocationsAsync(_scopeId);
        Assert.Single(all);
        Assert.Equal(AllocationStatus.Cancelled, all[0].Status);
        Assert.Equal("Servis Motor", all[0].Name);

        var balance = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(1_000_000m, balance);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task SeedBalance(Guid accountId, decimal amount, Guid? scopeId = null)
    {
        if (amount == 0) return;
        var sid = scopeId ?? _scopeId;

        var tx = new Transaction
        {
            ScopeId = sid,
            Type = TransactionType.Income,
            Amount = amount,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        var entry = new TransactionEntry
        {
            TransactionId = tx.Id,
            AccountId = accountId,
            Amount = amount
        };
        _db.TransactionEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    private async Task ArchiveAccount(Guid accountId)
    {
        var account = await _db.Accounts.FirstAsync(a => a.Id == accountId);
        account.IsArchived = true;
        await _db.SaveChangesAsync();
    }
}
