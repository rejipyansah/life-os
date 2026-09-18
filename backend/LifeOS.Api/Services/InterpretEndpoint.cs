using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public static class InterpretEndpoint
{
    public record HandlerResult(int StatusCode, object Body);

    public static async Task<HandlerResult> HandleAsync(
        InterpretInputRequest body,
        IInterpreter interpreter,
        Guid scopeId,
        ApplicationDbContext db,
        CancellationToken ct = default)
    {
        var accounts = (await db.Accounts
            .Where(a => a.ScopeId == scopeId && !a.IsArchived)
            .Select(a => new AccountLookup(a.Id, a.Name))
            .ToListAsync(ct))
            .ToList();

        if (accounts.Count == 0)
        {
            return new HandlerResult(200, new InterpretResponse
            {
                Intent = "CreateTransaction",
                State = "NeedsClarification",
                Clarifications = ["No accounts available. Please create an account first."]
            });
        }

        var accountNames = accounts.Select(a => a.Name).ToList();

        InterpretResult raw;
        try
        {
            raw = await interpreter.InterpretAsync(new InterpretRequest
            {
                Input = body.Input.Trim(),
                CurrentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EligibleAccounts = accountNames
            }, ct);
        }
        catch (InterpreterProviderException)
        {
            return new HandlerResult(503, new { error = "AI provider is temporarily unavailable. Please try again." });
        }

        return new HandlerResult(200, Validate(raw, accounts, scopeId));
    }

    public static InterpretResponse Validate(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        if (raw.Intent == "Unsupported")
        {
            return new InterpretResponse
            {
                Intent = "Unsupported",
                State = "Unsupported",
                Clarifications = raw.Clarifications.Count > 0
                    ? raw.Clarifications
                    : ["This input doesn't match any supported financial action."]
            };
        }

        if (raw.Clarifications.Count > 0)
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = raw.Clarifications
            };
        }

        if (string.IsNullOrEmpty(raw.TransactionType) || !raw.Amount.HasValue || raw.Amount <= 0)
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = ["Missing required information. Please specify the type and amount."]
            };
        }

        if (raw.TransactionType is not ("Expense" or "Income" or "Transfer"))
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = [$"Unsupported transaction type: {raw.TransactionType}"]
            };
        }

        if (string.IsNullOrEmpty(raw.Account))
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = ["Which account? Available: " + string.Join(", ", eligibleAccounts.Select(a => a.Name))]
            };
        }

        var accountNameLookup = eligibleAccounts.ToDictionary(
            a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

        if (!accountNameLookup.TryGetValue(raw.Account, out var accountId))
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = [$"Account '{raw.Account}' not found. Available: {string.Join(", ", eligibleAccounts.Select(a => a.Name))}"]
            };
        }

        if (raw.TransactionType == "Transfer")
        {
            if (string.IsNullOrEmpty(raw.ToAccount))
            {
                return new InterpretResponse
                {
                    Intent = raw.Intent,
                    State = "NeedsClarification",
                    Clarifications = ["Which destination account? Available: " + string.Join(", ", eligibleAccounts.Select(a => a.Name))]
                };
            }

            if (!accountNameLookup.TryGetValue(raw.ToAccount, out var toAccountId))
            {
                return new InterpretResponse
                {
                    Intent = raw.Intent,
                    State = "NeedsClarification",
                    Clarifications = [$"Destination account '{raw.ToAccount}' not found. Available: {string.Join(", ", eligibleAccounts.Select(a => a.Name))}"]
                };
            }

            if (accountId == toAccountId)
            {
                return new InterpretResponse
                {
                    Intent = raw.Intent,
                    State = "NeedsClarification",
                    Clarifications = ["Source and destination accounts must be different."]
                };
            }
        }

        var txData = new InterpretTransactionData
        {
            Type = raw.TransactionType!,
            Amount = raw.Amount!.Value,
            Description = raw.Description,
            Account = raw.Account,
            ToAccount = raw.ToAccount,
            Date = raw.Date,
            FeeAmount = raw.FeeAmount
        };

        var command = BuildCreateTransactionCommand(raw, accountId, scopeId, accountNameLookup);

        return new InterpretResponse
        {
            Intent = raw.Intent,
            State = "Ready",
            Preview = txData,
            Command = command,
            Clarifications = []
        };
    }

    private static CreateTransactionCommand BuildCreateTransactionCommand(
        InterpretResult raw,
        Guid accountId,
        Guid scopeId,
        Dictionary<string, Guid> accountNameLookup)
    {
        var amount = raw.Amount!.Value;
        var type = raw.TransactionType!;

        if (type == "Transfer")
        {
            var toAccountId = accountNameLookup[raw.ToAccount!];
            var fee = raw.FeeAmount ?? 0m;

            return new CreateTransactionCommand
            {
                ScopeId = scopeId,
                Type = TransactionType.Transfer,
                Amount = amount,
                Description = raw.Description,
                OccurredOn = DateOnly.Parse(raw.Date ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")),
                FeeAmount = fee > 0 ? fee : null,
                Entries =
                [
                    new CreateTransactionEntryCommand { AccountId = accountId, Amount = -(amount + fee) },
                    new CreateTransactionEntryCommand { AccountId = toAccountId, Amount = amount }
                ]
            };
        }

        var txType = type == "Expense" ? TransactionType.Expense : TransactionType.Income;
        var signedAmount = type == "Expense" ? -amount : amount;

        return new CreateTransactionCommand
        {
            ScopeId = scopeId,
            Type = txType,
            Amount = amount,
            Description = raw.Description,
            OccurredOn = DateOnly.Parse(raw.Date ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")),
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = accountId, Amount = signedAmount }
            ]
        };
    }
}

public record AccountLookup(Guid Id, string Name);
