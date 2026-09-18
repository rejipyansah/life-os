using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class InterpreterTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly Guid _scopeId;

    public InterpreterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        // Seed: Scope + Accounts
        var scope = new Scope { Type = ScopeType.Owner };
        _db.Scopes.Add(scope);
        _db.SaveChanges();
        _scopeId = scope.Id;

        _db.Accounts.AddRange(
            new Account { ScopeId = _scopeId, Name = "Cash", Type = AccountType.Cash },
            new Account { ScopeId = _scopeId, Name = "Mandiri", Type = AccountType.Bank },
            new Account { ScopeId = _scopeId, Name = "SeaBank", Type = AccountType.EWallet }
        );
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ───────────────────────── 1. Valid Expense ─────────────────────────

    [Fact]
    public async Task Interpret_Expense_ReturnsReady()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Expense",
            Amount = 18000,
            Description = "Jajan",
            Account = "Cash",
            Date = "2026-09-18",
            Clarifications = []
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb cash" },
            fake, _scopeId, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("Ready", response.State);
        Assert.Equal("CreateTransaction", response.Intent);
        Assert.NotNull(response.Preview);
        var preview = response.Preview!;
        Assert.Equal("Expense", preview.Type);
        Assert.Equal(18000m, preview.Amount);
        Assert.Equal("Cash", preview.Account);
        Assert.Equal("Jajan", preview.Description);
        Assert.NotNull(response.Command);
        Assert.Equal(preview.Type, response.Command!.Type);
    }

    // ───────────────────────── 2. Valid Income ─────────────────────────

    [Fact]
    public async Task Interpret_Income_ReturnsReady()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Income",
            Amount = 500000,
            Description = "Gaji",
            Account = "Mandiri",
            Date = "2026-09-01",
            Clarifications = []
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "gaji 500k mandiri" },
            fake, _scopeId, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("Ready", response.State);
        Assert.NotNull(response.Preview);
        Assert.Equal("Income", response.Preview!.Type);
        Assert.Equal(500000m, response.Preview.Amount);
        Assert.Equal("Mandiri", response.Preview.Account);
    }

    // ───────────────────────── 3. Valid Transfer ─────────────────────────

    [Fact]
    public async Task Interpret_Transfer_ReturnsReady()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Transfer",
            Amount = 100000,
            Account = "Mandiri",
            ToAccount = "SeaBank",
            Date = "2026-09-18",
            Clarifications = []
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "transfer 100k mandiri ke seabank" },
            fake, _scopeId, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("Ready", response.State);
        Assert.Equal("Transfer", response.Preview!.Type);
        Assert.Equal("Mandiri", response.Preview.Account);
        Assert.Equal("SeaBank", response.Preview.ToAccount);
    }

    // ───────────────────────── 4. Clarification needed ─────────────────────────

    [Fact]
    public async Task Interpret_MissingInfo_ReturnsNeedsClarification()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Expense",
            Amount = 18000,
            Account = null,
            Clarifications = ["Which account should this come from?"]
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb" },
            fake, _scopeId, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("NeedsClarification", response.State);
        Assert.Contains("Which account should this come from?", response.Clarifications);
        Assert.Null(response.Preview);
        Assert.Null(response.Command);
    }

    [Fact]
    public async Task Interpret_InvalidAccount_ReturnsNeedsClarification()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Expense",
            Amount = 18000,
            Account = "NonExistent",
            Date = "2026-09-18",
            Clarifications = []
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb nonexistent" },
            fake, _scopeId, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("NeedsClarification", response.State);
        Assert.Contains(response.Clarifications, c => c.Contains("NonExistent"));
    }

    // ───────────────────────── 5. Unsupported input ─────────────────────────

    [Fact]
    public async Task Interpret_UnsupportedInput_ReturnsUnsupported()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "Unsupported",
            Clarifications = []
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "what's the weather like?" },
            fake, _scopeId, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("Unsupported", response.State);
        Assert.Null(response.Preview);
        Assert.Null(response.Command);
    }

    // ───────────────────────── 6. Provider failure ─────────────────────────

    [Fact]
    public async Task Interpret_ProviderFailure_Returns503()
    {
        var fake = new FakeInterpreter(_ =>
            throw new InterpreterProviderException("Provider unavailable"));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb" },
            fake, _scopeId, _db);

        Assert.Equal(503, result.StatusCode);
    }

    // ───────────────────────── 7. No ID injection ─────────────────────────

    [Fact]
    public async Task Interpret_AIOutputCannotInjectInternalIds()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Expense",
            Amount = 18000,
            Account = "Cash",
            Date = "2026-09-18",
            Clarifications = []
        }));

        await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb cash" },
            fake, _scopeId, _db);

        Assert.NotNull(fake.LastRequest);
        // Eligible accounts must be names, not Guids
        Assert.All(fake.LastRequest.EligibleAccounts, account =>
            Assert.False(Guid.TryParse(account, out _), $"Account should be a name, not a GUID: {account}"));
        // ScopeId must never be passed to the interpreter
        Assert.DoesNotContain(fake.LastRequest.EligibleAccounts,
            a => a == _scopeId.ToString());
    }

    // ───────────────────────── 8. No persistence ─────────────────────────

    [Fact]
    public async Task Interpret_DoesNotPersistTransaction()
    {
        var txCountBefore = await _db.Transactions.CountAsync();
        var entryCountBefore = await _db.TransactionEntries.CountAsync();

        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Expense",
            Amount = 18000,
            Account = "Cash",
            Date = "2026-09-18",
            Clarifications = []
        }));

        await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb cash" },
            fake, _scopeId, _db);

        Assert.Equal(txCountBefore, await _db.Transactions.CountAsync());
        Assert.Equal(entryCountBefore, await _db.TransactionEntries.CountAsync());
    }

    // ───────────────────────── 9. Context used only as allowed input ─────────────────────────

    [Fact]
    public async Task Interpret_ScopeAndAccountContextPassedAsNamesOnly()
    {
        string? capturedInput = null;
        IReadOnlyList<string>? capturedAccounts = null;
        DateOnly? capturedDate = null;

        var fake = new FakeInterpreter(req =>
        {
            capturedInput = req.Input;
            capturedAccounts = req.EligibleAccounts;
            capturedDate = req.CurrentDate;
            return Task.FromResult(new InterpretResult
            {
                Intent = "CreateTransaction",
                TransactionType = "Expense",
                Amount = 18000,
                Account = "Cash",
                Date = "2026-09-18",
                Clarifications = []
            });
        });

        await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb cash" },
            fake, _scopeId, _db);

        Assert.Equal("jajan 18rb cash", capturedInput);
        Assert.NotNull(capturedAccounts);
        Assert.Equal(3, capturedAccounts.Count);
        Assert.Contains("Cash", capturedAccounts);
        Assert.Contains("Mandiri", capturedAccounts);
        Assert.Contains("SeaBank", capturedAccounts);
        Assert.NotNull(capturedDate);

        // No internal IDs leaked to the interpreter
        Assert.DoesNotContain(capturedAccounts, a => a == _scopeId.ToString());
        Assert.DoesNotContain(capturedAccounts, a => Guid.TryParse(a, out _));
    }

    // ───────────────────────── Transfer validation ─────────────────────────

    [Fact]
    public async Task Interpret_TransferSameAccount_ReturnsNeedsClarification()
    {
        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Transfer",
            Amount = 50000,
            Account = "Cash",
            ToAccount = "Cash",
            Date = "2026-09-18",
            Clarifications = []
        }));

        var response = InterpretEndpoint.Validate(
            new InterpretResult
            {
                Intent = "CreateTransaction",
                TransactionType = "Transfer",
                Amount = 50000,
                Account = "Cash",
                ToAccount = "Cash",
                Date = "2026-09-18",
                Clarifications = []
            },
            ["Cash", "Mandiri"]);

        Assert.Equal("NeedsClarification", response.State);
        Assert.Contains(response.Clarifications, c => c.Contains("different"));
    }

    // ───────────────────────── No accounts ─────────────────────────

    [Fact]
    public async Task Interpret_NoAccounts_ReturnsNeedsClarification()
    {
        // Create a scope with no accounts
        var emptyScope = new Scope { Type = ScopeType.Guest };
        _db.Scopes.Add(emptyScope);
        await _db.SaveChangesAsync();

        var fake = new FakeInterpreter(_ => Task.FromResult(new InterpretResult
        {
            Intent = "CreateTransaction",
            TransactionType = "Expense",
            Amount = 18000,
            Account = "Cash",
            Clarifications = []
        }));

        var result = await InterpretEndpoint.HandleAsync(
            new InterpretInputRequest { Input = "jajan 18rb" },
            fake, emptyScope.Id, _db);

        Assert.Equal(200, result.StatusCode);
        var response = Assert.IsType<InterpretResponse>(result.Body);
        Assert.Equal("NeedsClarification", response.State);
        Assert.Contains(response.Clarifications, c => c.Contains("No accounts"));
    }

    // ───────────────────────── FakeInterpreter ─────────────────────────

    private class FakeInterpreter : IInterpreter
    {
        private readonly Func<InterpretRequest, Task<InterpretResult>> _handler;
        public InterpretRequest? LastRequest { get; private set; }

        public FakeInterpreter(Func<InterpretRequest, Task<InterpretResult>> handler)
        {
            _handler = handler;
        }

        public async Task<InterpretResult> InterpretAsync(InterpretRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return await _handler(request);
        }
    }
}
