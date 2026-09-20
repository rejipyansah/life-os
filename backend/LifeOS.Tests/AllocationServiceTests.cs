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

    // ───────────────────────── Creation ─────────────────────────

    [Fact]
    public async Task Create_ValidAllocation_Succeeds()
    {
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
        Assert.True(allocation.IsActive);
        Assert.Equal(_scopeId, allocation.ScopeId);
    }

    [Fact]
    public async Task Create_ZeroAmount_Rejected()
    {
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
    public async Task Create_ZeroBalanceAccount_CanHaveAllocation()
    {
        // Account has zero balance but allocation is still allowed
        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Future Fund",
            Amount = 100_000m
        };

        var allocation = await _sut.CreateAllocationAsync(command);

        Assert.Equal(100_000m, allocation.Amount);
        Assert.True(allocation.IsActive);
    }

    [Fact]
    public async Task Create_OverAllocation_Allowed()
    {
        // Seed: account has 500k balance
        await SeedBalance(_accountAId, 500_000m);

        // Allocate 700k — over-allocation is allowed
        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Oversized Fund",
            Amount = 700_000m
        };

        var allocation = await _sut.CreateAllocationAsync(command);

        Assert.Equal(700_000m, allocation.Amount);
        Assert.True(allocation.IsActive);
    }

    [Fact]
    public async Task Create_NonExistentAccount_Rejected()
    {
        var command = new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = Guid.NewGuid(),
            Name = "Ghost Fund",
            Amount = 100m
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAllocationAsync(command));
    }

    // ───────────────────────── Scope isolation ─────────────────────────

    [Fact]
    public async Task Create_CrossScopeAccount_Rejected()
    {
        // Create a different scope and account
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

        // Try to create allocation for other scope's account using our scope
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
        // Create allocation in our scope
        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Our Fund",
            Amount = 100m
        });

        // Create allocation in another scope
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

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = otherScope.Id,
            AccountId = otherAccount.Id,
            Name = "Other Fund",
            Amount = 200m
        });

        // Get allocations for our scope — should only see ours
        var allocations = await _sut.GetAllocationsAsync(_scopeId);

        Assert.Single(allocations);
        Assert.Equal("Our Fund", allocations[0].Name);
    }

    // ───────────────────────── Lifecycle ─────────────────────────

    [Fact]
    public async Task Create_NewAllocationStartsActive()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Active Fund",
            Amount = 100m
        });

        Assert.True(allocation.IsActive);
    }

    [Fact]
    public async Task Update_SetIsActiveFalse_Deactivates()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "To Deactivate",
            Amount = 100m
        });

        var updated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Update_Reactivate_Works()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "To Reactivate",
            Amount = 100m
        });

        // Deactivate
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        // Reactivate
        var reactivated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = true
        });

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Update_ChangeName_Works()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Old Name",
            Amount = 100m
        });

        var updated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Name = "New Name"
        });

        Assert.Equal("New Name", updated.Name);
    }

    [Fact]
    public async Task Update_ChangeAmount_Works()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Flexible Fund",
            Amount = 100m
        });

        var updated = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 200m
        });

        Assert.Equal(200m, updated.Amount);
    }

    [Fact]
    public async Task Update_ZeroAmount_Rejected()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
            {
                ScopeId = _scopeId,
                Amount = 0
            }));
    }

    [Fact]
    public async Task Update_NonExistentAllocation_Rejected()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAllocationAsync(Guid.NewGuid(), new UpdateAllocationCommand
            {
                ScopeId = _scopeId,
                Name = "Ghost"
            }));
    }

    [Fact]
    public async Task Update_CrossScopeAllocation_Rejected()
    {
        // Create allocation in another scope
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

        var otherAllocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = otherScope.Id,
            AccountId = otherAccount.Id,
            Name = "Other Fund",
            Amount = 100m
        });

        // Try to update from our scope
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAllocationAsync(otherAllocation.Id, new UpdateAllocationCommand
            {
                ScopeId = _scopeId,
                Name = "Hijacked"
            }));
    }

    // ───────────────────────── Balance ─────────────────────────

    [Fact]
    public async Task Allocation_DoesNotChangeAccountBalance()
    {
        // Seed: account has 500k balance
        await SeedBalance(_accountAId, 500_000m);

        var balanceBefore = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 300_000m
        });

        var balanceAfter = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);

        Assert.Equal(balanceBefore, balanceAfter);
    }

    [Fact]
    public async Task Allocation_NoTransactionEntryCreated()
    {
        var entryCountBefore = await _db.TransactionEntries.CountAsync();

        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        var entryCountAfter = await _db.TransactionEntries.CountAsync();

        Assert.Equal(entryCountBefore, entryCountAfter);
    }

    [Fact]
    public async Task GetAllocatedAmount_SumsOnlyActiveAllocations()
    {
        // Create active allocation
        var active = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Active Fund",
            Amount = 300_000m
        });

        // Create another active allocation
        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Another Active Fund",
            Amount = 200_000m
        });

        // Deactivate the first one
        await _sut.UpdateAllocationAsync(active.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);

        // Only the second (200k) should count
        Assert.Equal(200_000m, allocated);
    }

    [Fact]
    public async Task AvailableAmount_ReflectsOverAllocation()
    {
        // Seed: account has 500k balance
        await SeedBalance(_accountAId, 500_000m);

        // Allocate 700k (over-allocation)
        await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Oversized Fund",
            Amount = 700_000m
        });

        var balance = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        var available = balance - allocated;

        Assert.Equal(500_000m, balance);
        Assert.Equal(700_000m, allocated);
        Assert.Equal(-200_000m, available); // Over-allocation produces negative available
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task SeedBalance(Guid accountId, decimal amount)
    {
        if (amount == 0) return;

        var tx = new Transaction
        {
            ScopeId = _scopeId,
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

    // ───────────────────────── Lifecycle: Edit ─────────────────────────

    [Fact]
    public async Task Edit_NameAndAmount_Succeeds()
    {
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
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task Edit_AccountChange_Succeeds()
    {
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
    public async Task Edit_AccountChange_ReflectsInAllocated()
    {
        // Account A has 100k allocation
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100_000m
        });

        var allocatedA = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        var allocatedB = await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId);
        Assert.Equal(100_000m, allocatedA);
        Assert.Equal(0m, allocatedB);

        // Move to Account B
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountBId
        });

        allocatedA = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        allocatedB = await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId);
        Assert.Equal(0m, allocatedA);
        Assert.Equal(100_000m, allocatedB);
    }

    [Fact]
    public async Task Edit_AmountChange_ReflectsInAvailable()
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

        // Edit to 500k
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 500_000m
        });

        var available2 = 500_000m - await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(0m, available2);
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

    // ───────────────────────── Lifecycle: Complete ─────────────────────────

    [Fact]
    public async Task Complete_Allocation_SetsInactive()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        var completed = await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        Assert.False(completed.IsActive);
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

        // Both active: 350k + 500k = 850k
        var allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(850_000m, allocated);

        // Complete WiFi
        await _sut.UpdateAllocationAsync(a1.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        // Only 500k active
        allocated = await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(500_000m, allocated);
    }

    [Fact]
    public async Task Complete_RemainsInHistory()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        var all = await _sut.GetAllocationsAsync(_scopeId);
        Assert.Single(all);
        Assert.Equal("WiFi", all[0].Name);
        Assert.False(all[0].IsActive);
    }

    [Fact]
    public async Task Complete_DoesNotChangeAccountBalance()
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
            IsActive = false
        });

        var balanceAfter = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(balanceBefore, balanceAfter);
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

        // Complete
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        var available2 = 500_000m - await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId);
        Assert.Equal(500_000m, available2);
    }

    [Fact]
    public async Task Complete_NoTransactionEntryCreated()
    {
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "Fund",
            Amount = 100m
        });

        var entryCountBefore = await _db.TransactionEntries.CountAsync();

        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });

        var entryCountAfter = await _db.TransactionEntries.CountAsync();
        Assert.Equal(entryCountBefore, entryCountAfter);
    }

    // ───────────────────────── Full Lifecycle ─────────────────────────

    [Fact]
    public async Task FullLifecycle_Create_Edit_Complete()
    {
        await SeedBalance(_accountAId, 1_000_000m);

        // 1. Create
        var allocation = await _sut.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountAId,
            Name = "WiFi",
            Amount = 350_000m
        });
        Assert.True(allocation.IsActive);
        Assert.Equal(350_000m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));

        // 2. Edit amount
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            Amount = 500_000m
        });
        Assert.Equal(500_000m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));

        // 3. Edit account
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = _accountBId
        });
        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountAId, _scopeId));
        Assert.Equal(500_000m, await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId));

        // 4. Complete
        await _sut.UpdateAllocationAsync(allocation.Id, new UpdateAllocationCommand
        {
            ScopeId = _scopeId,
            IsActive = false
        });
        Assert.Equal(0m, await _sut.GetAllocatedAmountAsync(_accountBId, _scopeId));

        // 5. Still in history
        var all = await _sut.GetAllocationsAsync(_scopeId);
        Assert.Single(all);
        Assert.False(all[0].IsActive);
        Assert.Equal("WiFi", all[0].Name);

        // 6. Balance never changed
        var balance = await _txService.GetAccountBalanceAsync(_accountAId, _scopeId);
        Assert.Equal(1_000_000m, balance);
    }
}
