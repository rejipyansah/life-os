using System.Data;
using System.Data.Common;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public sealed class GuestSandboxCleanupService(
    ApplicationDbContext db,
    ILogger<GuestSandboxCleanupService> logger)
{
    public static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(30);
    private const int BatchSize = 50;
    private const long PostgreSqlAdvisoryLockKey = 6_791_034_185_212_900_017;

    public async Task<int> DeleteExpiredSandboxesAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var openedConnection = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (openedConnection)
            await db.Database.OpenConnectionAsync(cancellationToken);

        var hasAdvisoryLock = false;
        try
        {
            if (db.Database.IsNpgsql())
            {
                hasAdvisoryLock = await TryAcquirePostgreSqlLockAsync(cancellationToken);
                if (!hasAdvisoryLock)
                {
                    logger.LogDebug("Guest sandbox cleanup skipped because another instance owns the cleanup lock.");
                    return 0;
                }
            }

            var deletionCutoff = utcNow - GuestTokenService.InactivityExpiry - RetentionAfterExpiry;
            var deleted = 0;

            while (true)
            {
                var scopeIds = await db.GuestSessions
                    .Where(session => session.LastActivityAt <= deletionCutoff
                        && session.Scope.Type == ScopeType.Guest)
                    .OrderBy(session => session.LastActivityAt)
                    .Select(session => session.ScopeId)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                if (scopeIds.Count == 0)
                    return deleted;

                foreach (var scopeId in scopeIds)
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    await DeleteSandboxAsync(scopeId, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    deleted++;
                }
            }
        }
        finally
        {
            if (hasAdvisoryLock)
                await ReleasePostgreSqlLockAsync(cancellationToken);

            if (openedConnection)
                await db.Database.CloseConnectionAsync();
        }
    }

    private async Task DeleteSandboxAsync(Guid scopeId, CancellationToken cancellationToken)
    {
        // Delete dependents in FK-safe order. Owner scopes are never selected by this service.
        await db.UpcomingEvents
            .Where(item => item.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.SetAsideEntries
            .Where(entry => entry.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);

        var scopedTransactionIds = db.Transactions
            .Where(transaction => transaction.ScopeId == scopeId)
            .Select(transaction => transaction.Id);
        await db.TransactionEntries
            .Where(entry => scopedTransactionIds.Contains(entry.TransactionId))
            .ExecuteDeleteAsync(cancellationToken);

        // Remove refunds/reversals before original transactions due to the self-reference FK.
        await db.Transactions
            .Where(transaction => transaction.ScopeId == scopeId
                && transaction.RelatedTransactionId != null)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Transactions
            .Where(transaction => transaction.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.SetAsides
            .Where(setAside => setAside.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Accounts
            .Where(account => account.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.GuestSessions
            .Where(session => session.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Scopes
            .Where(scope => scope.Id == scopeId && scope.Type == ScopeType.Guest)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<bool> TryAcquirePostgreSqlLockAsync(CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lockKey)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lockKey";
        parameter.Value = PostgreSqlAdvisoryLockKey;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task ReleasePostgreSqlLockAsync(CancellationToken cancellationToken)
    {
        await using DbCommand command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@lockKey)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lockKey";
        parameter.Value = PostgreSqlAdvisoryLockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}

public sealed class GuestSandboxCleanupWorker(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<GuestSandboxCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<GuestSandboxCleanupService>();
                var deleted = await cleanup.DeleteExpiredSandboxesAsync(DateTime.UtcNow, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} expired guest sandboxes.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Guest sandbox cleanup failed; it will retry on the next scheduled run.");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
