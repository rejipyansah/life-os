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
var transactionCount = ReadOption(args, "--transactions", 0);
var upcomingEventCount = ReadOption(args, "--upcoming-events", 0);
var iterations = ReadOption(args, "--iterations", 5);
if (setAsideCount <= 0 || entryCount <= 0 || transactionCount < 0 || upcomingEventCount < 0 || iterations <= 0)
    throw new ArgumentOutOfRangeException(nameof(args), "Set-aside count, entry count, and iterations must be positive; transaction/event counts cannot be negative.");

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
    var queryCounter = new SelectQueryCounter(args.Contains("--trace-queries", StringComparer.OrdinalIgnoreCase));
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(benchmarkConnection.ConnectionString)
        .AddInterceptors(queryCounter)
        .Options;

    await using var db = new ApplicationDbContext(options);
    await db.Database.MigrateAsync();
    var scope = new Scope { Type = ScopeType.Guest, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
    db.Scopes.Add(scope);
    var benchmarkNowUtc = DateTime.UtcNow;
    var businessToday = BusinessDate.TodayWib;
    var historicalCreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var setAsides = Enumerable.Range(0, setAsideCount)
        .Select(i =>
        {
            var cycleKind = (i % 4) switch
            {
                0 => SetAsideCycleKind.None,
                1 => SetAsideCycleKind.Weekly,
                2 => SetAsideCycleKind.Monthly,
                _ => SetAsideCycleKind.Quarterly
            };
            var kind = i % 4 == 3
                ? SetAsideKind.RoutineBatch
                : i % 4 == 1
                    ? SetAsideKind.RoutineIncremental
                    : SetAsideKind.Saving;
            return new SetAside
            {
                ScopeId = scope.Id,
                Name = $"Synthetic set-aside {i:D4}",
                Kind = kind,
                CycleKind = cycleKind,
                CycleAnchorDate = SetAsideCycle.HasCycle(cycleKind)
                    ? SetAsideCycle.CurrentCycleStart(businessToday, cycleKind, businessToday)
                    : new DateOnly(2025, 1, 1),
                TargetAmount = SetAsideCycle.HasCycle(cycleKind) ? 100_000m : null,
                Status = SetAsideStatus.Active,
                CreatedAt = historicalCreatedAt.AddMinutes(i),
                UpdatedAt = historicalCreatedAt.AddMinutes(i)
            };
        })
        .ToArray();
    db.SetAsides.AddRange(setAsides);
    await db.SaveChangesAsync();

    var accountCount = transactionCount + upcomingEventCount == 0
        ? 0
        : Math.Clamp((transactionCount + upcomingEventCount + 999) / 1_000, 1, 100);
    var accounts = Enumerable.Range(0, accountCount)
        .Select(i => new Account
        {
            ScopeId = scope.Id,
            Name = $"Synthetic account {i:D3}",
            Type = AccountType.Bank,
            CreatedAt = historicalCreatedAt
        })
        .ToArray();
    if (accounts.Length > 0)
    {
        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    var linkedSpendCount = Math.Min(transactionCount, (entryCount + 4) / 5);
    var reversalCount = (linkedSpendCount + 19) / 20;
    var spendTransactionIds = new List<Guid>(linkedSpendCount);
    var spendTransactionData = new List<(Guid Id, decimal Amount, Guid AccountId, DateOnly OccurredOn, string? CategoryName)>(linkedSpendCount);
    for (var offset = 0; offset < transactionCount; offset += 2_000)
    {
        var batchEnd = Math.Min(offset + 2_000, transactionCount);
        var transactions = new List<Transaction>(batchEnd - offset);
        var transactionEntries = new List<TransactionEntry>(batchEnd - offset);
        for (var i = offset; i < batchEnd; i++)
        {
            var isLinkedSpend = i < linkedSpendCount;
            var isIncome = !isLinkedSpend && i % 4 == 0;
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                ScopeId = scope.Id,
                Type = isIncome ? TransactionType.Income : TransactionType.Expense,
                Amount = 1_000m + i % 100_000,
                Description = $"Synthetic transaction {i:D8}",
                CategoryName = isIncome ? "Income" : "Benchmark",
                OccurredOn = businessToday.AddDays(-i % 365),
                CreatedAt = historicalCreatedAt.AddSeconds(i)
            };
            transactions.Add(transaction);
            if (isLinkedSpend)
            {
                spendTransactionIds.Add(transaction.Id);
                spendTransactionData.Add((
                    transaction.Id,
                    transaction.Amount,
                    accounts[i % accounts.Length].Id,
                    transaction.OccurredOn,
                    transaction.CategoryName));
            }
            transactionEntries.Add(new TransactionEntry
            {
                TransactionId = transaction.Id,
                AccountId = accounts[i % accounts.Length].Id,
                Amount = isIncome ? transaction.Amount : -transaction.Amount
            });
        }

        db.Transactions.AddRange(transactions);
        db.TransactionEntries.AddRange(transactionEntries);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    var reversalTransactions = new List<Transaction>(2_000);
    var reversalEntries = new List<TransactionEntry>(2_000);
    for (var i = 0; i < spendTransactionData.Count; i += 20)
    {
        var original = spendTransactionData[i];
        var reversal = new Transaction
        {
            ScopeId = scope.Id,
            Type = TransactionType.Reversal,
            Amount = original.Amount,
            Description = $"Synthetic reversal of {i:D8}",
            CategoryName = original.CategoryName,
            OccurredOn = original.OccurredOn,
            CreatedAt = benchmarkNowUtc.AddSeconds(i),
            RelatedTransactionId = original.Id
        };
        reversalTransactions.Add(reversal);
        reversalEntries.Add(new TransactionEntry
        {
            TransactionId = reversal.Id,
            AccountId = original.AccountId,
            Amount = original.Amount
        });
        if (reversalTransactions.Count == 2_000 || i + 20 >= spendTransactionData.Count)
        {
            db.Transactions.AddRange(reversalTransactions);
            db.TransactionEntries.AddRange(reversalEntries);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            reversalTransactions.Clear();
            reversalEntries.Clear();
        }
    }

    var entries = new List<SetAsideEntry>(Math.Min(entryCount, 5_000));
    var spendOrdinal = 0;
    for (var i = 0; i < entryCount; i++)
    {
        var setAside = setAsides[i % setAsides.Length];
        var isSpend = i % 5 == 0;
        var isCurrentCycleEntry = isSpend && i % 2 == 0 && SetAsideCycle.HasCycle(setAside.CycleKind);
        var (currentCycleStart, _) = SetAsideCycle.UsageWindow(setAside, businessToday);
        var createdAt = isCurrentCycleEntry && currentCycleStart.HasValue
            ? BusinessDate.StartOfDayUtc(currentCycleStart.Value).AddSeconds(i % 3_600)
            : historicalCreatedAt.AddSeconds(i);
        entries.Add(new SetAsideEntry
        {
            SetAsideId = setAside.Id,
            ScopeId = scope.Id,
            Type = isSpend ? SetAsideEntryType.Spent : SetAsideEntryType.Added,
            Amount = isSpend ? -1m : 2m,
            TransactionId = isSpend && spendOrdinal < spendTransactionIds.Count
                ? spendTransactionIds[spendOrdinal]
                : null,
            CreatedAt = createdAt
        });
        if (isSpend) spendOrdinal++;

        if (entries.Count == 5_000 || i == entryCount - 1)
        {
            db.SetAsideEntries.AddRange(entries);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            entries.Clear();
        }
    }

    var eventStart = BusinessDate.TodayWib;
    for (var offset = 0; offset < upcomingEventCount; offset += 2_000)
    {
        var batchEnd = Math.Min(offset + 2_000, upcomingEventCount);
        var events = Enumerable.Range(offset, batchEnd - offset)
            .Select(i => new UpcomingEvent
            {
                ScopeId = scope.Id,
                Title = $"Synthetic upcoming event {i:D8}",
                Amount = 5_000m + i % 250_000,
                Direction = i % 5 == 0 ? UpcomingEventDirection.Income : UpcomingEventDirection.Expense,
                AccountId = accounts[i % accounts.Length].Id,
                DefaultSourceAccountId = accounts[i % accounts.Length].Id,
                DueDate = eventStart.AddDays(1 + i % 365),
                ScheduleKind = UpcomingEventScheduleKind.Scheduled,
                Status = i % 10 == 0 ? UpcomingEventStatus.Cancelled : UpcomingEventStatus.Scheduled,
                CreatedAt = historicalCreatedAt.AddSeconds(i),
                UpdatedAt = historicalCreatedAt.AddSeconds(i)
            })
            .ToArray();
        db.UpcomingEvents.AddRange(events);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    async Task<int> ProjectSetAsidesAsync()
    {
        await using var requestDb = new ApplicationDbContext(options);
        var balances = new BalanceCalculator(requestDb);
        var setAsideService = new SetAsideService(requestDb, balances);
        return (await setAsideService.GetSetAsidesAsync(scope.Id)).Count;
    }

    async Task<int> ProjectFinanceStateAsync()
    {
        await using var requestDb = new ApplicationDbContext(options);
        var balances = new BalanceCalculator(requestDb);
        var setAsideService = new SetAsideService(requestDb, balances);
        var transactionService = new TransactionService(requestDb);
        var financeState = new FinanceStateService(requestDb, balances, setAsideService, transactionService);
        var projection = await financeState.GetStateAsync(scope.Id);
        return projection.SetAsides.Count;
    }

    async Task MeasureAsync(string label, Func<Task<int>> projection)
    {
        var elapsed = new List<double>(iterations);
        var allocatedBytes = new List<long>(iterations);
        var queryCounts = new List<long>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            queryCounter.Reset();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var timer = Stopwatch.StartNew();
            var projectedSetAsideCount = await projection();
            timer.Stop();
            elapsed.Add(timer.Elapsed.TotalMilliseconds);
            allocatedBytes.Add(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
            queryCounts.Add(queryCounter.Count);
            Console.WriteLine($"{label} iteration {i + 1}: {timer.Elapsed.TotalMilliseconds:F1} ms, {queryCounter.Count} SELECT queries, {allocatedBytes[^1] / 1024d / 1024d:F2} MiB allocated, {projectedSetAsideCount} set-asides");
        }

        Console.WriteLine($"{label} mean: {elapsed.Average():F1} ms; mean SELECTs: {queryCounts.Average():F1}; mean allocation: {allocatedBytes.Average() / 1024d / 1024d:F2} MiB");
    }

    // Warm both paths so first-use provider initialization does not skew their comparison.
    await ProjectSetAsidesAsync();
    await ProjectFinanceStateAsync();
    Console.WriteLine();
    Console.WriteLine($"Dataset: {setAsideCount:N0} set-asides, {entryCount:N0} set-aside entries, {transactionCount + reversalCount:N0} transactions ({transactionCount:N0} base + {reversalCount:N0} reversals), {upcomingEventCount:N0} upcoming events, {accountCount:N0} accounts");
    await MeasureAsync("Set-aside projection", ProjectSetAsidesAsync);
    await MeasureAsync("Full finance projection", ProjectFinanceStateAsync);
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
    private readonly bool _traceQueries;
    private readonly HashSet<string> _seenQueries = new(StringComparer.Ordinal);
    private long _count;

    public SelectQueryCounter(bool traceQueries) => _traceQueries = traceQueries;

    public long Count => Interlocked.Read(ref _count);
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _count);
            if (_traceQueries && _seenQueries.Add(command.CommandText))
                Console.WriteLine($"\nSQL query:\n{command.CommandText}\n");
        }
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
