using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class AccountServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly AccountService _sut;
    private readonly TransactionService _txService;
    private readonly AllocationService _allocService;
    private readonly Guid _scopeId;

    public AccountServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new AccountService(_db);
        _txService = new TransactionService(_db);
        _allocService = new AllocationService(_db);

        // Seed: Scope
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

    // ───────────────────────── Creation ─────────────────────────

    [Fact]
    public async Task Create_ValidAccount_Succeeds()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Mandiri",
            Type = AccountType.Bank
        };

        var account = await _sut.CreateAccountAsync(command);

        Assert.Equal("Mandiri", account.Name);
        Assert.Equal(AccountType.Bank, account.Type);
        Assert.False(account.IsArchived);
        Assert.Equal(_scopeId, account.ScopeId);
    }

    [Fact]
    public async Task Create_CashAccount_Succeeds()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Cash Wallet",
            Type = AccountType.Cash
        };

        var account = await _sut.CreateAccountAsync(command);

        Assert.Equal(AccountType.Cash, account.Type);
    }

    [Fact]
    public async Task Create_EWalletAccount_Succeeds()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "GoPay",
            Type = AccountType.EWallet
        };

        var account = await _sut.CreateAccountAsync(command);

        Assert.Equal(AccountType.EWallet, account.Type);
    }

    [Fact]
    public async Task Create_EmptyName_Rejected()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "",
            Type = AccountType.Bank
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAccountAsync(command));
    }

    [Fact]
    public async Task Create_WhitespaceName_Rejected()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "   ",
            Type = AccountType.Bank
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAccountAsync(command));
    }

    [Fact]
    public async Task Create_NullName_Rejected()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = null!,
            Type = AccountType.Bank
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAccountAsync(command));
    }

    [Fact]
    public async Task Create_InvalidType_Rejected()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Test",
            Type = (AccountType)999
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAccountAsync(command));
    }

    [Fact]
    public async Task Create_NameTrimmed()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "  Mandiri  ",
            Type = AccountType.Bank
        };

        var account = await _sut.CreateAccountAsync(command);

        Assert.Equal("Mandiri", account.Name);
    }

    [Fact]
    public async Task Create_ZeroBalanceAccount()
    {
        var command = new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Empty Account",
            Type = AccountType.Cash
        };

        var account = await _sut.CreateAccountAsync(command);

        var projection = await _sut.GetAccountByIdAsync(account.Id, _scopeId);
        Assert.Equal(0m, projection.Balance);
    }

    // ───────────────────────── Scope isolation ─────────────────────────

    [Fact]
    public async Task Create_CrossScope_Rejected()
    {
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var command = new CreateAccountCommand
        {
            ScopeId = otherScope.Id,
            Name = "Foreign Account",
            Type = AccountType.Bank
        };

        var account = await _sut.CreateAccountAsync(command);

        // Verify it belongs to the other scope, not ours
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAccountByIdAsync(account.Id, _scopeId));
    }

    // ───────────────────────── Read ─────────────────────────

    [Fact]
    public async Task GetAccounts_ReturnsOnlyCurrentScope()
    {
        await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Our Account",
            Type = AccountType.Bank
        });

        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = otherScope.Id,
            Name = "Other Account",
            Type = AccountType.Bank
        });

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Single(result.Accounts);
        Assert.Equal("Our Account", result.Accounts[0].Name);
    }

    [Fact]
    public async Task GetAccounts_HiddenByDefault()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "To Archive",
            Type = AccountType.Cash
        });

        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var result = await _sut.GetAccountsAsync(_scopeId, includeArchived: false);
        Assert.Empty(result.Accounts);
    }

    [Fact]
    public async Task GetAccounts_IncludedWithFlag()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "To Archive",
            Type = AccountType.Cash
        });

        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var result = await _sut.GetAccountsAsync(_scopeId, includeArchived: true);
        Assert.Single(result.Accounts);
        Assert.True(result.Accounts[0].IsArchived);
    }

    [Fact]
    public async Task GetAccounts_BalanceDerived()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Seeded Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 500_000m);

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Single(result.Accounts);
        Assert.Equal(500_000m, result.Accounts[0].Balance);
    }

    [Fact]
    public async Task GetAccounts_AllocatedDerived()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Allocation Account",
            Type = AccountType.Bank
        });

        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Vacation Fund",
            Amount = 300_000m
        });

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Single(result.Accounts);
        Assert.Equal(300_000m, result.Accounts[0].Allocated);
    }

    [Fact]
    public async Task GetAccounts_AvailableCalculation()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Calc Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 500_000m);
        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Fund",
            Amount = 200_000m
        });

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Single(result.Accounts);
        Assert.Equal(500_000m, result.Accounts[0].Balance);
        Assert.Equal(200_000m, result.Accounts[0].Allocated);
        Assert.Equal(300_000m, result.Accounts[0].Available);
    }

    [Fact]
    public async Task GetAccounts_NegativeAvailablePreserved()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Over-Allocated",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 100_000m);
        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Big Fund",
            Amount = 300_000m
        });

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Single(result.Accounts);
        Assert.Equal(-200_000m, result.Accounts[0].Available);
    }

    [Fact]
    public async Task GetAccounts_ZeroBalanceAppears()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Zero Balance",
            Type = AccountType.Cash
        });

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Single(result.Accounts);
        Assert.Equal(0m, result.Accounts[0].Balance);
    }

    [Fact]
    public async Task GetAccounts_SummaryMatchesReturnedSet()
    {
        var accountA = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Account A",
            Type = AccountType.Bank
        });
        var accountB = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Account B",
            Type = AccountType.Cash
        });

        await SeedBalance(accountA.Id, 500_000m);
        await SeedBalance(accountB.Id, 300_000m);
        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = accountA.Id,
            Name = "Fund A",
            Amount = 100_000m
        });

        var result = await _sut.GetAccountsAsync(_scopeId);

        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal(800_000m, result.TotalBalance);
        Assert.Equal(100_000m, result.TotalAllocated);
        Assert.Equal(700_000m, result.TotalAvailable);
    }

    [Fact]
    public async Task GetAccounts_ArchivedNotInDefaultSummary()
    {
        var accountA = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Active",
            Type = AccountType.Bank
        });
        var accountB = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Archived",
            Type = AccountType.Cash
        });

        await SeedBalance(accountA.Id, 500_000m);
        await SeedBalance(accountB.Id, 300_000m);

        // Settle accountB to zero before archiving
        await SeedBalance(accountB.Id, -300_000m);

        await _sut.UpdateAccountAsync(accountB.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var result = await _sut.GetAccountsAsync(_scopeId, includeArchived: false);

        Assert.Single(result.Accounts);
        Assert.Equal(500_000m, result.TotalBalance);
    }

    // ───────────────────────── Get by ID ─────────────────────────

    [Fact]
    public async Task GetById_ExistingAccount_ReturnsProjection()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Detail Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 250_000m);

        var projection = await _sut.GetAccountByIdAsync(account.Id, _scopeId);

        Assert.Equal(account.Id, projection.Id);
        Assert.Equal("Detail Account", projection.Name);
        Assert.Equal(250_000m, projection.Balance);
    }

    [Fact]
    public async Task GetById_NonExistentAccount_Throws()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAccountByIdAsync(Guid.NewGuid(), _scopeId));
    }

    [Fact]
    public async Task GetById_CrossScopeAccount_Throws()
    {
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherAccount = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = otherScope.Id,
            Name = "Guest Account",
            Type = AccountType.Cash
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAccountByIdAsync(otherAccount.Id, _scopeId));
    }

    // ───────────────────────── Update ─────────────────────────

    [Fact]
    public async Task Update_ChangeName_Works()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Old Name",
            Type = AccountType.Bank
        });

        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "New Name"
        });

        Assert.Equal("New Name", updated.Name);
    }

    [Fact]
    public async Task Update_NameCanChange_WithTransactionHistory()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Historical Account",
            Type = AccountType.Bank
        });

        // Create a transaction
        await SeedBalance(account.Id, 100_000m);

        // Name change should still work
        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Renamed Account"
        });

        Assert.Equal("Renamed Account", updated.Name);
    }

    [Fact]
    public async Task Update_ChangeType_BeforeTransactions_Works()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Flexible Account",
            Type = AccountType.Cash
        });

        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            Type = AccountType.Bank
        });

        Assert.Equal(AccountType.Bank, updated.Type);
    }

    [Fact]
    public async Task Update_ChangeType_AfterTransactions_Succeeds()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Flexible Account",
            Type = AccountType.Cash
        });

        // Create a transaction
        await SeedBalance(account.Id, 100_000m);

        // Type change should now succeed (type is metadata, not a financial event)
        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            Type = AccountType.Bank
        });

        Assert.Equal(AccountType.Bank, updated.Type);

        // Verify balance is unchanged
        var projection = await _sut.GetAccountByIdAsync(account.Id, _scopeId);
        Assert.Equal(100_000m, projection.Balance);
    }

    [Fact]
    public async Task Update_Archive_Works()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "To Archive",
            Type = AccountType.Cash
        });

        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        Assert.True(updated.IsArchived);
    }

    [Fact]
    public async Task Update_Unarchive_Works()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "To Unarchive",
            Type = AccountType.Cash
        });

        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var unarchived = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = false
        });

        Assert.False(unarchived.IsArchived);
    }

    [Fact]
    public async Task Update_EmptyName_Rejected()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Valid Name",
            Type = AccountType.Bank
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                Name = ""
            }));
    }

    [Fact]
    public async Task Update_ScopeCannotChange()
    {
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Our Account",
            Type = AccountType.Bank
        });

        // Try to update with wrong scope
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
            {
                ScopeId = otherScope.Id,
                Name = "Hijacked"
            }));
    }

    [Fact]
    public async Task Update_CrossScopeAccount_Rejected()
    {
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherAccount = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = otherScope.Id,
            Name = "Guest Account",
            Type = AccountType.Cash
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(otherAccount.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                Name = "Hijacked"
            }));
    }

    [Fact]
    public async Task Update_InvalidType_Rejected()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Test",
            Type = AccountType.Bank
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                Type = (AccountType)999
            }));
    }

    // ───────────────────────── Archived Account ─────────────────────────

    [Fact]
    public async Task ArchivedAccount_CannotBeUsedInTransaction()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Archived Account",
            Type = AccountType.Bank
        });

        // Archive with zero balance (valid)
        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = account.Id, Amount = 100_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _txService.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task ArchivedAccount_CannotBeTransferSource()
    {
        var accountA = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Source Account",
            Type = AccountType.Bank
        });
        var accountB = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Dest Account",
            Type = AccountType.Cash
        });

        // Archive the source account (zero balance, valid)
        await _sut.UpdateAccountAsync(accountA.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = accountA.Id, Amount = -100_000m },
                new CreateTransactionEntryCommand { AccountId = accountB.Id, Amount = 100_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _txService.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task ArchivedAccount_CannotBeTransferDestination()
    {
        var accountA = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Source Account",
            Type = AccountType.Bank
        });
        var accountB = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Dest Account",
            Type = AccountType.Cash
        });

        // Archive the destination account (zero balance, valid)
        await _sut.UpdateAccountAsync(accountB.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var command = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Transfer,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = accountA.Id, Amount = -100_000m },
                new CreateTransactionEntryCommand { AccountId = accountB.Id, Amount = 100_000m }
            ]
        };

        await Assert.ThrowsAsync<ValidationException>(() => _txService.CreateTransactionAsync(command));
    }

    [Fact]
    public async Task UnarchivedAccount_CanBeUsedAgain()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Unarchive Account",
            Type = AccountType.Bank
        });

        // Archive (zero balance, valid)
        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        // Verify archived - transaction should fail
        var command1 = new CreateTransactionCommand
        {
            ScopeId = _scopeId,
            Type = TransactionType.Income,
            Amount = 100_000m,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = account.Id, Amount = 100_000m }
            ]
        };
        await Assert.ThrowsAsync<ValidationException>(() => _txService.CreateTransactionAsync(command1));

        // Unarchive
        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = false
        });

        // Verify unarchived - transaction should succeed
        var (tx, _) = await _txService.CreateTransactionAsync(command1);
        Assert.Equal(TransactionType.Income, tx.Type);
    }

    // ───────────────────────── Archive Validation ─────────────────────────

    [Fact]
    public async Task Archive_ZeroBalance_NoAllocation_Succeeds()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Empty Account",
            Type = AccountType.Cash
        });

        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        Assert.True(updated.IsArchived);
    }

    [Fact]
    public async Task Archive_ZeroBalance_WithTransactionHistory_Succeeds()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Historical Account",
            Type = AccountType.Bank
        });

        // Create income then expense to bring balance back to zero
        await SeedBalance(account.Id, 100_000m);
        await SeedBalance(account.Id, -100_000m);

        // Verify balance is zero
        var projection = await _sut.GetAccountByIdAsync(account.Id, _scopeId);
        Assert.Equal(0m, projection.Balance);

        // Archive should succeed despite transaction history
        var updated = await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        Assert.True(updated.IsArchived);
    }

    [Fact]
    public async Task Archive_PositiveBalance_Rejected()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Funded Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 500_000m);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                IsArchived = true
            }));
    }

    [Fact]
    public async Task Archive_ActiveAllocation_Rejected()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Allocated Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 500_000m);
        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Vacation Fund",
            Amount = 200_000m
        });

        // Settle balance to zero
        await SeedBalance(account.Id, -500_000m);

        // Still has active allocation
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                IsArchived = true
            }));
    }

    [Fact]
    public async Task Archive_PositiveBalance_And_ActiveAllocation_Rejected()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Dual Block Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 500_000m);
        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Fund",
            Amount = 200_000m
        });

        // Both balance > 0 and active allocation > 0
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                IsArchived = true
            }));
    }

    [Fact]
    public async Task Archive_NeverCreatesTransaction()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Clean Archive",
            Type = AccountType.Cash
        });

        var txCountBefore = await _db.Transactions.CountAsync();

        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        var txCountAfter = await _db.Transactions.CountAsync();
        Assert.Equal(txCountBefore, txCountAfter);

        var entryCount = await _db.TransactionEntries.CountAsync(te => te.AccountId == account.Id);
        Assert.Equal(0, entryCount);
    }

    [Fact]
    public async Task ChangeType_DoesNotChangeBalance()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Balance Check",
            Type = AccountType.Cash
        });

        await SeedBalance(account.Id, 250_000m);

        var txCountBefore = await _db.Transactions.CountAsync();

        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            Type = AccountType.Bank
        });

        // Balance unchanged
        var projection = await _sut.GetAccountByIdAsync(account.Id, _scopeId);
        Assert.Equal(250_000m, projection.Balance);

        // No new transactions created
        var txCountAfter = await _db.Transactions.CountAsync();
        Assert.Equal(txCountBefore, txCountAfter);
    }

    [Fact]
    public async Task ChangeType_DoesNotModifyTransactionHistory()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "History Check",
            Type = AccountType.Cash
        });

        await SeedBalance(account.Id, 100_000m);

        var txService = new TransactionService(_db);
        var txCountBefore = (await txService.GetTransactionsAsync(_scopeId)).Count;

        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            Type = AccountType.Bank
        });

        // Transaction history unchanged
        var txCountAfter = (await txService.GetTransactionsAsync(_scopeId)).Count;
        Assert.Equal(txCountBefore, txCountAfter);
    }

    [Fact]
    public async Task Archive_CrossScope_Rejected()
    {
        var otherScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(otherScope);
        await _db.SaveChangesAsync();

        var otherAccount = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = otherScope.Id,
            Name = "Guest Account",
            Type = AccountType.Cash
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateAccountAsync(otherAccount.Id, new UpdateAccountCommand
            {
                ScopeId = _scopeId,
                IsArchived = true
            }));
    }

    [Fact]
    public async Task ZeroActiveAccounts_IsValidState()
    {
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Only Account",
            Type = AccountType.Cash
        });

        // Archive the only account
        await _sut.UpdateAccountAsync(account.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        // Zero active accounts is valid
        var result = await _sut.GetAccountsAsync(_scopeId, includeArchived: false);
        Assert.Empty(result.Accounts);

        // Archived still exists
        var allResult = await _sut.GetAccountsAsync(_scopeId, includeArchived: true);
        Assert.Single(allResult.Accounts);
        Assert.True(allResult.Accounts[0].IsArchived);
    }

    [Fact]
    public async Task ArchivedAccount_NotEligibleForNewTransactions()
    {
        var accountA = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Active Account",
            Type = AccountType.Bank
        });
        var accountB = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Archived Account",
            Type = AccountType.Cash
        });

        // Archive accountB
        await _sut.UpdateAccountAsync(accountB.Id, new UpdateAccountCommand
        {
            ScopeId = _scopeId,
            IsArchived = true
        });

        // Default listing should not include archived
        var result = await _sut.GetAccountsAsync(_scopeId);
        Assert.Single(result.Accounts);
        Assert.Equal(accountA.Id, result.Accounts[0].Id);
    }

    // ───────────────────────── Integrity ─────────────────────────

    [Fact]
    public async Task NoBalanceColumn_IntegrityCheck()
    {
        // Verify that Account entity does not have a Balance property
        var properties = typeof(Account).GetProperties();
        Assert.DoesNotContain(properties, p =>
            p.Name.Equals("Balance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoProviderInstitutionFields_IntegrityCheck()
    {
        var properties = typeof(Account).GetProperties().Select(p => p.Name).ToList();
        Assert.False(properties.Any(p => p.Equals("Provider", StringComparison.OrdinalIgnoreCase)),
            "Account should not have a Provider field.");
        Assert.False(properties.Any(p => p.Equals("Institution", StringComparison.OrdinalIgnoreCase)),
            "Account should not have an Institution field.");
        Assert.False(properties.Any(p => p.Equals("Currency", StringComparison.OrdinalIgnoreCase)),
            "Account should not have a Currency field.");
    }

    [Fact]
    public async Task NoHardDeleteEndpoint_IntegrityCheck()
    {
        var methodNames = typeof(AccountService)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain(methodNames, m =>
            m.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExistingDataRemainsIntact()
    {
        // Create account and transaction
        var account = await _sut.CreateAccountAsync(new CreateAccountCommand
        {
            ScopeId = _scopeId,
            Name = "Existing Account",
            Type = AccountType.Bank
        });

        await SeedBalance(account.Id, 500_000m);

        // Verify data integrity
        var projection = await _sut.GetAccountByIdAsync(account.Id, _scopeId);
        Assert.Equal(500_000m, projection.Balance);

        // Create allocation
        await _allocService.CreateAllocationAsync(new CreateAllocationCommand
        {
            ScopeId = _scopeId,
            AccountId = account.Id,
            Name = "Fund",
            Amount = 200_000m
        });

        var result = await _sut.GetAccountsAsync(_scopeId);
        Assert.Single(result.Accounts);
        Assert.Equal(500_000m, result.Accounts[0].Balance);
        Assert.Equal(200_000m, result.Accounts[0].Allocated);
    }

    // ───────────────────────── Scope Initialization ─────────────────────────

    [Fact]
    public async Task InitializeScope_OwnerScope_GetsExactlyOneTunaiAccount()
    {
        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        await _db.SaveChangesAsync();

        await ScopeInitializer.EnsureDefaultAccountAsync(_db, scope.Id);

        var accounts = await _db.Accounts.Where(a => a.ScopeId == scope.Id).ToListAsync();
        Assert.Single(accounts);
        Assert.Equal("Tunai", accounts[0].Name);
        Assert.Equal(AccountType.Cash, accounts[0].Type);
        Assert.False(accounts[0].IsArchived);

        var balance = await _db.TransactionEntries
            .Where(te => te.AccountId == accounts[0].Id)
            .SumAsync(te => te.Amount);
        Assert.Equal(0m, balance);
    }

    [Fact]
    public async Task InitializeScope_GuestScope_GetsExactlyOneTunaiAccount()
    {
        var scope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(scope);
        await _db.SaveChangesAsync();

        await ScopeInitializer.EnsureDefaultAccountAsync(_db, scope.Id);

        var accounts = await _db.Accounts.Where(a => a.ScopeId == scope.Id).ToListAsync();
        Assert.Single(accounts);
        Assert.Equal("Tunai", accounts[0].Name);
        Assert.Equal(AccountType.Cash, accounts[0].Type);
        Assert.False(accounts[0].IsArchived);

        var balance = await _db.TransactionEntries
            .Where(te => te.AccountId == accounts[0].Id)
            .SumAsync(te => te.Amount);
        Assert.Equal(0m, balance);
    }

    [Fact]
    public async Task InitializeScope_RepeatedInitialization_DoesNotCreateDuplicates()
    {
        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        await _db.SaveChangesAsync();

        await ScopeInitializer.EnsureDefaultAccountAsync(_db, scope.Id);
        await ScopeInitializer.EnsureDefaultAccountAsync(_db, scope.Id);

        var accounts = await _db.Accounts.Where(a => a.ScopeId == scope.Id).ToListAsync();
        Assert.Single(accounts);
        Assert.Equal("Tunai", accounts[0].Name);
    }

    [Fact]
    public async Task InitializeScope_ExistingScopeWithZeroActiveAccounts_DoesNotRecreateTunai()
    {
        // Simulate: existing scope had Mandiri, which was archived
        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        await _db.SaveChangesAsync();

        var mandiri = new Account
        {
            ScopeId = scope.Id,
            Name = "Mandiri",
            Type = AccountType.Bank,
            IsArchived = true
        };
        _db.Accounts.Add(mandiri);
        await _db.SaveChangesAsync();

        // Scope already has an account (even if archived), so EnsureDefaultAccount should not create Tunai
        await ScopeInitializer.EnsureDefaultAccountAsync(_db, scope.Id);

        var accounts = await _db.Accounts.Where(a => a.ScopeId == scope.Id).ToListAsync();
        Assert.Single(accounts);
        Assert.Equal("Mandiri", accounts[0].Name);
        Assert.True(accounts[0].IsArchived);
    }

    [Fact]
    public async Task InitializeScope_DefaultAccount_HasCorrectProperties()
    {
        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        await _db.SaveChangesAsync();

        await ScopeInitializer.EnsureDefaultAccountAsync(_db, scope.Id);

        var account = await _db.Accounts.SingleAsync(a => a.ScopeId == scope.Id);
        Assert.Equal("Tunai", account.Name);
        Assert.Equal(AccountType.Cash, account.Type);
        Assert.False(account.IsArchived);
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.True(account.CreatedAt <= DateTime.UtcNow);

        // Balance must be 0 (no transaction entries)
        var balance = await _db.TransactionEntries
            .Where(te => te.AccountId == account.Id)
            .SumAsync(te => te.Amount);
        Assert.Equal(0m, balance);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task SeedBalance(Guid accountId, decimal amount)
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

        var entry = new TransactionEntry
        {
            TransactionId = tx.Id,
            AccountId = accountId,
            Amount = amount
        };
        _db.TransactionEntries.Add(entry);
        await _db.SaveChangesAsync();
    }
}
