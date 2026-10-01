using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public class TransactionService
{
    private readonly ApplicationDbContext _db;

    public TransactionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<(Transaction transaction, List<TransactionEntry> entries)> CreateTransactionAsync(
        CreateTransactionCommand command,
        CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, token => ExecuteInTransactionAsync(command, token), ct);

    /// <summary>
    /// Membatalkan/mengoreksi transaksi yang sudah diposting TANPA mengubah histori.
    ///
    /// Transaksi asli tetap utuh dan tidak pernah dimutasi. Yang dibuat adalah satu
    /// Transaction Reversal baru dengan entry berlawanan, dihubungkan lewat
    /// RelatedTransactionId sehingga jejak audit tetap dapat ditelusuri.
    /// </summary>
    public Task<Transaction> ReverseTransactionAsync(
        ReverseTransactionCommand command,
        CancellationToken ct = default)
        => SerializableCommandRunner.RunAsync(_db, async token =>
        {
            var original = await _db.Transactions
                .FirstOrDefaultAsync(t => t.Id == command.TransactionId && t.ScopeId == command.ScopeId, token);

            if (original is null)
                throw new ValidationException("Transaction not found in current Scope.");

            if (command.Reason is not null && command.Reason.Trim().Length > 512)
                throw new ValidationException("Reversal reason must not exceed 512 characters.");

            var entries = await _db.TransactionEntries
                .Where(te => te.TransactionId == original.Id)
                .Select(te => new { te.AccountId, te.Amount })
                .ToListAsync(token);

            if (entries.Count == 0)
                throw new ValidationException("Transaction has no entries to reverse.");

            var create = new CreateTransactionCommand
            {
                ScopeId = command.ScopeId,
                Type = TransactionType.Reversal,
                Amount = original.Amount,
                Description = string.IsNullOrWhiteSpace(command.Reason)
                    ? "Reversal"
                    : command.Reason.Trim(),
                CategoryName = original.CategoryName,
                OccurredOn = command.OccurredOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
                RelatedTransactionId = original.Id,
                Entries = entries
                    .Select(e => new CreateTransactionEntryCommand { AccountId = e.AccountId, Amount = -e.Amount })
                    .ToList()
            };

            var (reversal, _) = await ExecuteInTransactionAsync(create, token);
            return reversal;
        }, ct);

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

        var allocated = await _db.SetAsideEntries
            .Where(se => se.SetAside.AccountId == accountId && se.SetAside.Status == SetAsideStatus.Active)
            .SumAsync(se => se.Amount, ct);

        var available = balance - allocated;
        if (available + command.Entries[0].Amount < 0)
            throw new ValidationException("Insufficient available balance. Funds are reserved by active set-asides.");

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

        var sourceAllocated = await _db.SetAsideEntries
            .Where(se => se.SetAside.AccountId == source.AccountId && se.SetAside.Status == SetAsideStatus.Active)
            .SumAsync(se => se.Amount, ct);

        var sourceAvailable = sourceBalance - sourceAllocated;
        if (sourceAvailable + source.Amount < 0)
            throw new ValidationException("Insufficient available balance in source Account. Funds are reserved by active set-asides.");

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

        var alreadyReversed = await _db.Transactions.AnyAsync(
            t => t.Type == TransactionType.Reversal
                && t.RelatedTransactionId == command.RelatedTransactionId.Value
                && t.ScopeId == scopeId,
            ct);

        if (alreadyReversed)
            throw new ValidationException("This transaction has already been reversed.");

        if (command.Entries.Count == 0)
            throw new ValidationException("Reversal must have at least one entry.");

        // Reversal membalik semua entry transaksi asli, jadi jumlah entry mengikuti
        // transaksi yang dikoreksi (1 untuk Income/Expense, 2 untuk Transfer).
        foreach (var entry in command.Entries)
        {
            if (entry.Amount == 0)
                throw new ValidationException("Reversal entry Amount must be non-zero.");
        }

        if (command.FeeAmount.HasValue)
            throw new ValidationException("FeeAmount is not applicable for Reversal.");

        return command.Entries.Select(e => BuildEntry(e, transaction.Id)).ToList();
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

    // ───────────────────────── Query Methods ─────────────────────────

    public async Task<List<TransactionProjection>> GetTransactionsAsync(
        Guid scopeId,
        int? limit = null,
        CancellationToken ct = default)
    {
        var query = _db.Transactions
            .Where(t => t.ScopeId == scopeId)
            .OrderByDescending(t => t.OccurredOn)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new TransactionProjection
            {
                Id = t.Id,
                Type = t.Type,
                Amount = t.Amount,
                Description = t.Description,
                CategoryName = t.CategoryName,
                OccurredOn = t.OccurredOn,
                CreatedAt = t.CreatedAt,
                RelatedTransactionId = t.RelatedTransactionId,
                FeeAmount = t.FeeAmount
            });

        if (limit is > 0)
            query = query.Take(limit.Value);

        var result = await query.ToListAsync(ct);

        // Load entries separately to avoid N+1 and use efficient batch query
        var transactionIds = result.Select(t => t.Id).ToList();
        var entries = await _db.TransactionEntries
            .Where(te => transactionIds.Contains(te.TransactionId))
            .Join(_db.Accounts,
                te => te.AccountId,
                a => a.Id,
                (te, a) => new { te.TransactionId, te.AccountId, AccountName = a.Name, te.Amount })
            .ToListAsync(ct);

        var entriesByTransaction = entries
            .GroupBy(e => e.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => new TransactionEntryProjection
                {
                    AccountId = e.AccountId,
                    AccountName = e.AccountName,
                    Amount = e.Amount
                }).ToList());

        foreach (var tx in result)
        {
            tx.Entries = entriesByTransaction.GetValueOrDefault(tx.Id, []);
        }

        await ApplyReversalInfoAsync(result, ct);

        return result;
    }

    /// <summary>
    /// Menandai transaksi yang sudah dibatalkan beserta alasan pembatalannya,
    /// supaya histori tetap dapat ditelusuri tanpa mengubah transaksi asli.
    /// </summary>
    private async Task ApplyReversalInfoAsync(List<TransactionProjection> transactions, CancellationToken ct)
    {
        if (transactions.Count == 0) return;

        var ids = transactions.Select(t => t.Id).ToList();
        var reversals = await _db.Transactions
            .Where(t => t.Type == TransactionType.Reversal
                && t.RelatedTransactionId != null
                && ids.Contains(t.RelatedTransactionId.Value))
            .Select(t => new { t.RelatedTransactionId, t.Description })
            .ToListAsync(ct);

        var byOriginal = reversals
            .Where(r => r.RelatedTransactionId.HasValue)
            .GroupBy(r => r.RelatedTransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Description);

        foreach (var tx in transactions)
        {
            if (byOriginal.TryGetValue(tx.Id, out var reason))
            {
                tx.IsReversed = true;
                tx.ReversalReason = reason;
            }
        }
    }

    public async Task<TransactionProjection?> GetTransactionByIdAsync(
        Guid transactionId,
        Guid scopeId,
        CancellationToken ct = default)
    {
        var transaction = await _db.Transactions
            .Where(t => t.Id == transactionId && t.ScopeId == scopeId)
            .Select(t => new TransactionProjection
            {
                Id = t.Id,
                Type = t.Type,
                Amount = t.Amount,
                Description = t.Description,
                CategoryName = t.CategoryName,
                OccurredOn = t.OccurredOn,
                CreatedAt = t.CreatedAt,
                RelatedTransactionId = t.RelatedTransactionId,
                FeeAmount = t.FeeAmount
            })
            .FirstOrDefaultAsync(ct);

        if (transaction is null)
            return null;

        // Load entries with Account names
        transaction.Entries = await _db.TransactionEntries
            .Where(te => te.TransactionId == transactionId)
            .Join(_db.Accounts,
                te => te.AccountId,
                a => a.Id,
                (te, a) => new TransactionEntryProjection
                {
                    AccountId = te.AccountId,
                    AccountName = a.Name,
                    Amount = te.Amount
                })
            .ToListAsync(ct);

        await ApplyReversalInfoAsync([transaction], ct);

        return transaction;
    }
}

public class TransactionProjection
{
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? RelatedTransactionId { get; set; }
    public decimal? FeeAmount { get; set; }
    public List<TransactionEntryProjection> Entries { get; set; } = [];

    /// <summary>True bila transaksi ini sudah dibatalkan lewat Reversal.</summary>
    public bool IsReversed { get; set; }

    /// <summary>Alasan pembatalan, diambil dari transaksi Reversal terkait.</summary>
    public string? ReversalReason { get; set; }
}

public class TransactionEntryProjection
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public decimal Amount { get; set; }
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
