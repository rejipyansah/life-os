using LifeOS.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using IsolationLevel = System.Data.IsolationLevel;

namespace LifeOS.Api.Services;

/// <summary>
/// Runs a money-moving write operation inside a SERIALIZABLE database transaction and
/// retries on PostgreSQL serialization failures (SQLSTATE 40001).
///
/// Under SERIALIZABLE isolation, concurrent transactions that read overlapping rows can
/// cause one to fail at commit time. Retrying the whole operation lets the loser re-read
/// fresh data and succeed, which is what keeps double-spending / double-withdrawal /
/// stale-available-balance impossible.
/// </summary>
public static class SerializableCommandRunner
{
    private const int MaxSerializationRetries = 3;

    public static async Task<T> RunAsync<T>(
        ApplicationDbContext db,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var dbTransaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            try
            {
                var result = await action(ct);
                await dbTransaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxSerializationRetries)
            {
                await SafeRollbackAsync(dbTransaction, ct);
                // Brief yield to let the winning transaction release its locks
                await Task.Yield();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                await SafeRollbackAsync(dbTransaction, ct);
                throw new SerializationConflictException();
            }
            catch (Exception)
            {
                await SafeRollbackAsync(dbTransaction, ct);
                throw;
            }
        }
    }

    public static async Task RunAsync(
        ApplicationDbContext db,
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var dbTransaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            try
            {
                await action(ct);
                await dbTransaction.CommitAsync(ct);
                return;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxSerializationRetries)
            {
                await SafeRollbackAsync(dbTransaction, ct);
                // Brief yield to let the winning transaction release its locks
                await Task.Yield();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                await SafeRollbackAsync(dbTransaction, ct);
                throw new SerializationConflictException();
            }
            catch (Exception)
            {
                await SafeRollbackAsync(dbTransaction, ct);
                throw;
            }
        }
    }

    private static bool IsSerializationFailure(Exception ex)
        => FindNpgsqlSerializationFailure(ex) is not null;

    private static NpgsqlException? FindNpgsqlSerializationFailure(Exception ex)
    {
        // Check both the exception and its inner exception, because when PostgreSQL
        // aborts a SERIALIZABLE transaction, the Npgsql driver may wrap the
        // serialization failure inside an InvalidOperationException.
        var current = ex;
        while (current is not null)
        {
            if (current is NpgsqlException npgsqlEx
                && npgsqlEx.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                return npgsqlEx;
            }
            current = current.InnerException;
        }
        return null;
    }

    private static async Task SafeRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken ct)
    {
        // When PostgreSQL aborts a SERIALIZABLE transaction, the transaction
        // object may already be completed. RollbackAsync would throw
        // InvalidOperationException. Silently ignore that case.
        try
        {
            await transaction.RollbackAsync(ct);
        }
        catch (InvalidOperationException)
        {
            // Transaction already completed/aborted by PostgreSQL — nothing to do.
        }
    }
}
