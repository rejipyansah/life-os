using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using IsolationLevel = System.Data.IsolationLevel;

namespace LifeOS.Api.Services;

public class TransactionService
{
    private readonly ApplicationDbContext _db;

    private const int MaxSerializationRetries = 3;

    public TransactionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(Transaction transaction, List<TransactionEntry> entries)> CreateTransactionAsync(
        CreateTransactionCommand command,
        CancellationToken ct = default)
    {
        // Retry loop handles PostgreSQL serialization failures (SQLSTATE 40001).
        // Under SERIALIZABLE isolation, concurrent transactions that read overlapping
        // rows can cause one to fail at commit time. Retrying the entire transaction
        // allows the loser to re-read fresh data and succeed.
        for (var attempt = 1; ; attempt++)
        {
            await using var dbTransaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

            try
            {
                var result = await ExecuteInTransactionAsync(command, ct);
                await dbTransaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < MaxSerializationRetries)
            {
                await dbTransaction.RollbackAsync(ct);
                // Brief yield to let the winning transaction release its locks
                await Task.Yield();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                await dbTransaction.RollbackAsync(ct);
                throw new SerializationConflictException();
            }
            catch (Exception)
            {
                await dbTransaction.RollbackAsync(ct);
                throw;
            }
        }
    }

    private async Task<(Transaction transaction, List<TransactionEntry> entries)> ExecuteInTransactionAsync(
        CreateTransactionCommand command,
        CancellationToken ct)
    {
        var scopeId = command.ScopeId;

        // Validate all referenced Accounts belong to the current Scope
        var accountIds = command.Entries.Select(e => e.AccountId).Distinct().ToList();
        var accounts = await _db.Accounts
            .Where(a => accountIds.Contains(a.Id) && a.ScopeId == scopeId)
            .ToListAsync(ct);

        if (accounts.Count != accountIds.Count)
        {
            var foundIds = accounts.Select(a => a.Id).ToHashSet();
            var missingIds = accountIds.Where(id => !foundIds.Contains(id));
            throw new ValidationException(
                $"Account(s) not found in current Scope: {string.Join(", ", missingIds)}");
        }

        // Validate referenced Accounts are not archived
        var archivedAccounts = accounts.Where(a => a.IsArchived).ToList();
        if (archivedAccounts.Count > 0)
        {
            throw new ValidationException(
                $"Cannot use archived Account(s): {string.Join(", ", archivedAccounts.Select(a => a.Name))}");
        }

        // Build Transaction entity
        var transaction = new Transaction
        {
            ScopeId = scopeId,
            Type = command.Type,
            Amount = command.Amount,
            Description = command.Description,
            CategoryName = command.CategoryName,
            OccurredOn = command.OccurredOn,
            RelatedTransactionId = command.RelatedTransactionId,
            FeeAmount = command.FeeAmount
        };

        // Validate and build entries based on TransactionType
        var entries = command.Type switch
        {
            TransactionType.Income => ValidateIncome(command, transaction),
            TransactionType.Expense => await ValidateExpenseAsync(command, transaction, ct),
            TransactionType.Transfer => await ValidateTransferAsync(command, transaction, ct),
            TransactionType.Refund => await ValidateRefundAsync(command, transaction, scopeId, ct),
            TransactionType.Reversal => await ValidateReversalAsync(command, transaction, scopeId, ct),
            TransactionType.Adjustment => ValidateAdjustment(command, transaction),
            _ => throw new ValidationException($"Unsupported TransactionType: {command.Type}")
        };

        _db.Transactions.Add(transaction);
        _db.TransactionEntries.AddRange(entries);
        await _db.SaveChangesAsync(ct);

        return (transaction, entries);
    }

    private static bool IsSerializationFailure(Exception ex)
    {
        // PostgreSQL serialization failure: SQLSTATE 40001
        // Detected via NpgsqlException which carries the SQLSTATE code.
        return ex is NpgsqlException npgsqlEx
            && npgsqlEx.SqlState == PostgresErrorCodes.SerializationFailure;
    }

    public async Task<decimal> GetAccountBalanceAsync(Guid accountId, Guid scopeId, CancellationToken ct = default)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.ScopeId == scopeId, ct);

        if (account is null)
            throw new ValidationException("Account not found in current Scope.");

        return await _db.TransactionEntries
            .Where(te => te.AccountId == accountId)
            .SumAsync(te => te.Amount, ct);
    }

    private List<TransactionEntry> ValidateIncome(
        CreateTransactionCommand command,
        Transaction transaction)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Income Amount must be positive.");

        if (command.Entries.Count != 1)
            throw new ValidationException("Income must have exactly one entry.");

        if (command.Entries[0].Amount <= 0)
            throw new ValidationException("Income entry Amount must be positive.");

        if (command.FeeAmount.HasValue)
            throw new ValidationException("FeeAmount is not applicable for Income.");

        return [BuildEntry(command.Entries[0], transaction.Id)];
    }

    private async Task<List<TransactionEntry>> ValidateExpenseAsync(
        CreateTransactionCommand command,
        Transaction transaction,
        CancellationToken ct)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Expense Amount must be positive.");

        if (command.Entries.Count != 1)
            throw new ValidationException("Expense must have exactly one entry.");

        if (command.Entries[0].Amount >= 0)
            throw new ValidationException("Expense entry Amount must be negative.");

        if (command.FeeAmount.HasValue)
            throw new ValidationException("FeeAmount is not applicable for Expense.");

        var accountId = command.Entries[0].AccountId;
        var balance = await GetAccountBalanceAsync(accountId, command.ScopeId, ct);

        if (balance + command.Entries[0].Amount < 0)
            throw new ValidationException("Insufficient balance.");

        return [BuildEntry(command.Entries[0], transaction.Id)];
    }

    private async Task<List<TransactionEntry>> ValidateTransferAsync(
        CreateTransactionCommand command,
        Transaction transaction,
        CancellationToken ct)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Transfer Amount must be positive.");

        if (command.Entries.Count != 2)
            throw new ValidationException("Transfer must have exactly two entries (source and destination).");

        var source = command.Entries[0];
        var destination = command.Entries[1];

        if (source.AccountId == destination.AccountId)
            throw new ValidationException("Transfer source and destination must differ.");

        if (source.Amount >= 0)
            throw new ValidationException("Transfer source entry Amount must be negative.");

        if (destination.Amount <= 0)
            throw new ValidationException("Transfer destination entry Amount must be positive.");

        if (command.FeeAmount.HasValue && command.FeeAmount.Value < 0)
            throw new ValidationException("FeeAmount must not be negative.");

        var expectedSourceAmount = command.FeeAmount.HasValue
            ? -(command.Amount + command.FeeAmount.Value)
            : -command.Amount;

        if (source.Amount != expectedSourceAmount)
            throw new ValidationException($"Transfer source Amount must be {expectedSourceAmount}.");

        if (destination.Amount != command.Amount)
            throw new ValidationException($"Transfer destination Amount must be {command.Amount}.");

        var sourceBalance = await GetAccountBalanceAsync(source.AccountId, command.ScopeId, ct);
        if (sourceBalance + source.Amount < 0)
            throw new ValidationException("Insufficient balance in source Account.");

        return [BuildEntry(source, transaction.Id), BuildEntry(destination, transaction.Id)];
    }

    private async Task<List<TransactionEntry>> ValidateRefundAsync(
        CreateTransactionCommand command,
        Transaction transaction,
        Guid scopeId,
        CancellationToken ct)
    {
        if (!command.RelatedTransactionId.HasValue)
            throw new ValidationException("Refund requires RelatedTransactionId.");

        var related = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == command.RelatedTransactionId.Value && t.ScopeId == scopeId, ct);

        if (related is null)
            throw new ValidationException("Related transaction not found in current Scope.");

        if (related.Type != TransactionType.Expense)
            throw new ValidationException("Refund can only reference an Expense transaction.");

        if (command.Amount <= 0)
            throw new ValidationException("Refund Amount must be positive.");

        if (command.Entries.Count != 1)
            throw new ValidationException("Refund must have exactly one entry.");

        if (command.Entries[0].Amount <= 0)
            throw new ValidationException("Refund entry Amount must be positive.");

        if (command.FeeAmount.HasValue)
            throw new ValidationException("FeeAmount is not applicable for Refund.");

        return [BuildEntry(command.Entries[0], transaction.Id)];
    }

    private async Task<List<TransactionEntry>> ValidateReversalAsync(
        CreateTransactionCommand command,
        Transaction transaction,
        Guid scopeId,
        CancellationToken ct)
    {
        if (!command.RelatedTransactionId.HasValue)
            throw new ValidationException("Reversal requires RelatedTransactionId.");

        var related = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == command.RelatedTransactionId.Value && t.ScopeId == scopeId, ct);

        if (related is null)
            throw new ValidationException("Related transaction not found in current Scope.");

        if (command.Entries.Count != 1)
            throw new ValidationException("Reversal must have exactly one entry.");

        if (command.Entries[0].Amount == 0)
            throw new ValidationException("Reversal entry Amount must be non-zero.");

        if (command.FeeAmount.HasValue)
            throw new ValidationException("FeeAmount is not applicable for Reversal.");

        return [BuildEntry(command.Entries[0], transaction.Id)];
    }

    private List<TransactionEntry> ValidateAdjustment(
        CreateTransactionCommand command,
        Transaction transaction)
    {
        if (command.Amount <= 0)
            throw new ValidationException("Adjustment Amount must be positive.");

        if (command.Entries.Count != 1)
            throw new ValidationException("Adjustment must have exactly one entry.");

        if (command.Entries[0].Amount == 0)
            throw new ValidationException("Adjustment entry Amount must be non-zero.");

        if (command.FeeAmount.HasValue)
            throw new ValidationException("FeeAmount is not applicable for Adjustment.");

        return [BuildEntry(command.Entries[0], transaction.Id)];
    }

    private static TransactionEntry BuildEntry(CreateTransactionEntryCommand command, Guid transactionId)
    {
        return new TransactionEntry
        {
            TransactionId = transactionId,
            AccountId = command.AccountId,
            Amount = command.Amount
        };
    }
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

public class SerializationConflictException : Exception
{
    public SerializationConflictException()
        : base("The request conflicted with a concurrent operation. Please try again.") { }
}
