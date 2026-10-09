using System.Data.Common;
using System.Diagnostics;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("LIFEOS_TEST_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("Set LIFEOS_TEST_POSTGRES_CONNECTION to an isolated benchmark database.");
var setAsideCount = ReadOption(args, "--set-asides", 100);
var entryCount = ReadOption(args, "--entries", 50_000);
var iterations = ReadOption(args, "--iterations", 5);
if (setAsideCount <= 0 || entryCount <= 0 || iterations <= 0)
    throw new ArgumentOutOfRangeException(nameof(args), "All benchmark sizes and iterations must be positive.");

var schema = $"lifeos_benchmark_{Guid.NewGuid():N}";
var adminConnection = new NpgsqlConnectionStringBuilder(connectionString);
var benchmarkConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
await using (var admin = new NpgsqlConnection(adminConnection.ConnectionString))
{
    await admin.OpenAsync();
    await using var create = admin.CreateCommand();
    create.CommandText = $"CREATE SCHEMA \"{schema}\"";
    await create.ExecuteNonQueryAsync();
}

try
{
    var queryCounter = new SelectQueryCounter();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(benchmarkConnection.ConnectionString)
        .AddInterceptors(queryCounter)
        .Options;

    await using var db = new ApplicationDbContext(options);
    await db.Database.MigrateAsync();
    var scope = new Scope { Type = ScopeType.Guest, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
    db.Scopes.Add(scope);
    var setAsides = Enumerable.Range(0, setAsideCount)
        .Select(i => new SetAside
        {
            ScopeId = scope.Id,
            Name = $"Synthetic set-aside {i:D4}",
            Kind = SetAsideKind.Saving,
            CycleKind = SetAsideCycleKind.None,
            CycleAnchorDate = new DateOnly(2025, 1, 1),
            Status = SetAsideStatus.Active,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)
        })
        .ToArray();
    db.SetAsides.AddRange(setAsides);
    await db.SaveChangesAsync();

    var entries = new List<SetAsideEntry>(Math.Min(entryCount, 5_000));
    var createdAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    for (var i = 0; i < entryCount; i++)
    {
        var setAside = setAsides[i % setAsides.Length];
        var isSpend = i % 5 == 0;
        entries.Add(new SetAsideEntry
        {
            SetAsideId = setAside.Id,
            ScopeId = scope.Id,
            Type = isSpend ? SetAsideEntryType.Spent : SetAsideEntryType.Added,
            Amount = isSpend ? -1m : 2m,
            CreatedAt = createdAt.AddSeconds(i)
        });

        if (entries.Count == 5_000 || i == entryCount - 1)
        {
            db.SetAsideEntries.AddRange(entries);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            entries.Clear();
        }
    }

    async Task<int> ProjectAsync()
    {
        await using var requestDb = new ApplicationDbContext(options);
        var balances = new BalanceCalculator(requestDb);
        var setAsideService = new SetAsideService(requestDb, balances);
        var transactionService = new TransactionService(requestDb);
        var financeState = new FinanceStateService(requestDb, balances, setAsideService, transactionService);
        var projection = await financeState.GetStateAsync(scope.Id);
        return projection.SetAsides.Count;
    }

    // One warmup with a separate context to avoid measuring one-time provider initialization.
    await ProjectAsync();
    var elapsed = new List<double>(iterations);
    var allocatedBytes = new List<long>(iterations);
    var queryCounts = new List<long>(iterations);
    for (var i = 0; i < iterations; i++)
    {
        queryCounter.Reset();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var projectedSetAsideCount = await ProjectAsync();
        timer.Stop();
        elapsed.Add(timer.Elapsed.TotalMilliseconds);
        allocatedBytes.Add(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
        queryCounts.Add(queryCounter.Count);
        Console.WriteLine($"Iteration {i + 1}: {timer.Elapsed.TotalMilliseconds:F1} ms, {queryCounter.Count} SELECT queries, {allocatedBytes[^1] / 1024d / 1024d:F2} MiB allocated, {projectedSetAsideCount} set-asides");
    }

    Console.WriteLine();
    Console.WriteLine($"Dataset: {setAsideCount:N0} set-asides, {entryCount:N0} ledger entries");
    Console.WriteLine($"Mean: {elapsed.Average():F1} ms; mean SELECTs: {queryCounts.Average():F1}; mean allocation: {allocatedBytes.Average() / 1024d / 1024d:F2} MiB");
}
finally
{
    await using var admin = new NpgsqlConnection(adminConnection.ConnectionString);
    await admin.OpenAsync();
    await using var drop = admin.CreateCommand();
    drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
    await drop.ExecuteNonQueryAsync();
}

static int ReadOption(string[] arguments, string name, int fallback)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length
        ? int.Parse(arguments[index + 1], System.Globalization.CultureInfo.InvariantCulture)
        : fallback;
}

sealed class SelectQueryCounter : DbCommandInterceptor
{
    private long _count;
    public long Count => Interlocked.Read(ref _count);
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
