using LifeOS.Api.Data;
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
        var accounts = await db.Accounts
            .Where(a => a.ScopeId == scopeId && !a.IsArchived)
            .Select(a => a.Name)
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            return new HandlerResult(200, new InterpretResponse
            {
                Intent = "CreateTransaction",
                State = "NeedsClarification",
                Clarifications = ["No accounts available. Please create an account first."]
            });
        }

        InterpretResult raw;
        try
        {
            raw = await interpreter.InterpretAsync(new InterpretRequest
            {
                Input = body.Input.Trim(),
                CurrentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EligibleAccounts = accounts
            }, ct);
        }
        catch (InterpreterProviderException)
        {
            return new HandlerResult(503, new { error = "AI provider is temporarily unavailable. Please try again." });
        }

        return new HandlerResult(200, Validate(raw, accounts));
    }

    public static InterpretResponse Validate(InterpretResult raw, IReadOnlyList<string> eligibleAccounts)
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

        if (string.IsNullOrEmpty(raw.Account) || !eligibleAccounts.Contains(raw.Account, StringComparer.OrdinalIgnoreCase))
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = raw.Account is null
                    ? ["Which account? Available: " + string.Join(", ", eligibleAccounts)]
                    : [$"Account '{raw.Account}' not found. Available: {string.Join(", ", eligibleAccounts)}"]
            };
        }

        if (raw.TransactionType == "Transfer")
        {
            if (string.IsNullOrEmpty(raw.ToAccount) || !eligibleAccounts.Contains(raw.ToAccount, StringComparer.OrdinalIgnoreCase))
            {
                return new InterpretResponse
                {
                    Intent = raw.Intent,
                    State = "NeedsClarification",
                    Clarifications = raw.ToAccount is null
                        ? ["Which destination account? Available: " + string.Join(", ", eligibleAccounts)]
                        : [$"Destination account '{raw.ToAccount}' not found. Available: {string.Join(", ", eligibleAccounts)}"]
                };
            }

            if (string.Equals(raw.Account, raw.ToAccount, StringComparison.OrdinalIgnoreCase))
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

        return new InterpretResponse
        {
            Intent = raw.Intent,
            State = "Ready",
            Preview = txData,
            Command = txData,
            Clarifications = []
        };
    }
}
