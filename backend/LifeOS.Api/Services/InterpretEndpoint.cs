using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public static class InterpretEndpoint
{
    public record HandlerResult(int StatusCode, object Body);

    private static readonly HashSet<string> ValidFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "account", "toAccount", "allocationName"
    };

    private static readonly Dictionary<string, string> FieldMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amount"] = "Nominalnya berapa?",
        ["account"] = "Dari akun mana?",
        ["toAccount"] = "Ke akun mana?",
        ["allocationName"] = "Untuk keperluan apa?"
    };

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
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
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

        return new HandlerResult(200, Validate(raw, accounts, scopeId, body.Input.Trim()));
    }

    public static InterpretResponse Validate(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId,
        string userInput = "")
    {
        if (raw.Intent == "Unsupported")
        {
            return new InterpretResponse
            {
                Intent = "Unsupported",
                State = "Unsupported",
                Clarifications = ["This input doesn't match any supported financial action."]
            };
        }

        var fields = raw.ClarificationFields
            .Where(f => ValidFields.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (raw.Intent == "CreateAllocation")
        {
            CollectAllocationFields(raw, eligibleAccounts, fields);
            return BuildResponse(raw, fields, eligibleAccounts, scopeId);
        }

        CollectTransactionFields(raw, eligibleAccounts, scopeId, userInput, fields);
        return BuildResponse(raw, fields, eligibleAccounts, scopeId);
    }

    private static void CollectAllocationFields(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        List<string> fields)
    {
        if (!raw.Amount.HasValue || raw.Amount <= 0)
            AddField(fields, "amount");

        if (string.IsNullOrWhiteSpace(raw.AllocationName))
            AddField(fields, "allocationName");

        if (string.IsNullOrEmpty(raw.Account))
        {
            AddField(fields, "account");
        }
        else
        {
            var accountNameLookup = eligibleAccounts.ToDictionary(
                a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

            if (!accountNameLookup.ContainsKey(raw.Account))
                AddField(fields, "account");
        }
    }

    private static void CollectTransactionFields(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId,
        string userInput,
        List<string> fields)
    {
        if (string.IsNullOrEmpty(raw.TransactionType) || !raw.Amount.HasValue || raw.Amount <= 0)
        {
            if (string.IsNullOrEmpty(raw.TransactionType))
            {
                AddField(fields, "account");
            }

            if (!raw.Amount.HasValue || raw.Amount <= 0)
                AddField(fields, "amount");

            return;
        }

        if (raw.TransactionType is not ("Expense" or "Income" or "Transfer"))
            return;

        if (string.IsNullOrEmpty(raw.Account))
        {
            AddField(fields, "account");
        }
        else
        {
            var accountNameLookup = eligibleAccounts.ToDictionary(
                a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

            if (!accountNameLookup.ContainsKey(raw.Account))
                AddField(fields, "account");
        }

        if (raw.TransactionType == "Transfer")
        {
            if (string.IsNullOrEmpty(raw.ToAccount))
            {
                AddField(fields, "toAccount");
            }
            else
            {
                var accountNameLookup = eligibleAccounts.ToDictionary(
                    a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

                if (!accountNameLookup.TryGetValue(raw.ToAccount, out var _))
                {
                    AddField(fields, "toAccount");
                }
            }

            if (!string.IsNullOrEmpty(userInput) && !fields.Contains("account", StringComparer.OrdinalIgnoreCase))
            {
                if (!IsAccountMentionedInInput(raw.Account!, userInput))
                    AddField(fields, "account");
            }

            if (!string.IsNullOrEmpty(userInput) && !fields.Contains("toAccount", StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(raw.ToAccount) && !IsAccountMentionedInInput(raw.ToAccount, userInput))
                    AddField(fields, "toAccount");
            }
        }
    }

    private static void AddField(List<string> fields, string field)
    {
        if (!fields.Contains(field, StringComparer.OrdinalIgnoreCase))
            fields.Add(field);
    }

    private static InterpretResponse BuildResponse(
        InterpretResult raw,
        List<string> fields,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        if (fields.Count > 0)
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = fields
                    .Where(f => FieldMessages.ContainsKey(f))
                    .Select(f => FieldMessages[f])
                    .ToList()
            };
        }

        if (raw.Intent == "CreateAllocation")
            return BuildAllocationReady(raw, eligibleAccounts, scopeId);

        return BuildTransactionReady(raw, eligibleAccounts, scopeId);
    }

    private static InterpretResponse BuildAllocationReady(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        var accountNameLookup = eligibleAccounts.ToDictionary(
            a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

        if (!accountNameLookup.TryGetValue(raw.Account!, out var accountId))
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "NeedsClarification",
                Clarifications = [$"Account '{raw.Account}' not found. Available: {string.Join(", ", eligibleAccounts.Select(a => a.Name))}"]
            };
        }

        var preview = new InterpretAllocationData
        {
            Name = raw.AllocationName!,
            Amount = raw.Amount!.Value,
            Account = raw.Account
        };

        var command = new CreateAllocationCommand
        {
            ScopeId = scopeId,
            AccountId = accountId,
            Name = raw.AllocationName!.Trim(),
            Amount = raw.Amount!.Value
        };

        return new InterpretResponse
        {
            Intent = raw.Intent,
            State = "Ready",
            AllocationPreview = preview,
            AllocationCommand = command,
            Clarifications = []
        };
    }

    private static InterpretResponse BuildTransactionReady(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        var accountNameLookup = eligibleAccounts.ToDictionary(
            a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

        if (!accountNameLookup.TryGetValue(raw.Account!, out var accountId))
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
            if (!accountNameLookup.TryGetValue(raw.ToAccount!, out var toAccountId))
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

    internal static bool IsAccountMentionedInInput(string accountName, string userInput)
    {
        var normalizedInput = userInput.ToLowerInvariant();
        var inputTokens = normalizedInput
            .Split([' ', '\t', ',', '.', '!', '?', ';', ':', '/', '-'], StringSplitOptions.RemoveEmptyEntries);

        var accountTokens = accountName.ToLowerInvariant()
            .Split([' ', '\t', ',', '.', '!', '?', ';', ':', '/', '-'], StringSplitOptions.RemoveEmptyEntries);

        return accountTokens.All(token => inputTokens.Contains(token));
    }
}

public record AccountLookup(Guid Id, string Name);
