using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// Natural Input → interpret → structured command/preview → user confirmation → backend command.
///
/// Interpreter tetap interpreter: langkah ini tidak pernah memodifikasi database.
/// User mengonfirmasi preview, lalu frontend mengirim command ke endpoint yang sesuai.
/// </summary>
public static class InterpretEndpoint
{
    public record HandlerResult(int StatusCode, object Body);

    private static readonly HashSet<string> ValidFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "account", "toAccount", "setAsideName", "title", "direction", "date"
    };

    private static readonly Dictionary<string, string> FieldMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amount"] = "Nominalnya berapa?",
        ["account"] = "Dari akun mana?",
        ["toAccount"] = "Ke akun mana?",
        ["setAsideName"] = "Untuk keperluan apa?",
        ["title"] = "Agendanya untuk apa?",
        ["direction"] = "Apakah ini pemasukan atau pengeluaran?",
        ["date"] = "Tanggalnya kapan?"
    };

    private const string IntentTransaction = "CreateTransaction";
    private const string IntentSetAside = "CreateSetAside";
    private const string IntentEvent = "CreateUpcomingEvent";

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
                Intent = IntentTransaction,
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
        if (raw.Intent == "Unsupported" || raw.Intent is not (IntentTransaction or IntentSetAside or IntentEvent))
        {
            return new InterpretResponse
            {
                Intent = raw.Intent,
                State = "Unsupported",
                Clarifications = ["This input doesn't match any supported financial action."]
            };
        }

        var fields = raw.ClarificationFields
            .Where(f => ValidFields.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        switch (raw.Intent)
        {
            case IntentSetAside:
                CollectSetAsideFields(raw, eligibleAccounts, fields);
                break;
            case IntentEvent:
                CollectEventFields(raw, eligibleAccounts, fields);
                break;
            default:
                CollectTransactionFields(raw, eligibleAccounts, userInput, fields);
                break;
        }

        return BuildResponse(raw, fields, eligibleAccounts, scopeId);
    }

    private static void CollectSetAsideFields(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        List<string> fields)
    {
        if (!raw.Amount.HasValue || raw.Amount <= 0)
            AddField(fields, "amount");

        if (string.IsNullOrWhiteSpace(raw.SetAsideName))
            AddField(fields, "setAsideName");

        if (!TryResolveAccount(raw.Account, eligibleAccounts, out _))
            AddField(fields, "account");
    }

    private static void CollectEventFields(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        List<string> fields)
    {
        if (!raw.Amount.HasValue || raw.Amount <= 0)
            AddField(fields, "amount");

        if (string.IsNullOrWhiteSpace(raw.Title))
            AddField(fields, "title");

        if (raw.Direction is not ("Income" or "Expense"))
            AddField(fields, "direction");

        if (!TryResolveAccount(raw.Account, eligibleAccounts, out _))
            AddField(fields, "account");

        // Agenda fleksibel boleh tanpa tanggal; tanpa tanggal ia otomatis Flexible.
        if (!string.IsNullOrWhiteSpace(raw.Date) && !TryParseDate(raw.Date, out _))
            AddField(fields, "date");
    }

    private static void CollectTransactionFields(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        string userInput,
        List<string> fields)
    {
        if (string.IsNullOrEmpty(raw.TransactionType) || !raw.Amount.HasValue || raw.Amount <= 0)
        {
            if (string.IsNullOrEmpty(raw.TransactionType))
                AddField(fields, "account");

            if (!raw.Amount.HasValue || raw.Amount <= 0)
                AddField(fields, "amount");

            return;
        }

        if (raw.TransactionType is not ("Expense" or "Income" or "Transfer"))
            return;

        if (!TryResolveAccount(raw.Account, eligibleAccounts, out _))
            AddField(fields, "account");

        if (raw.TransactionType == "Transfer")
        {
            if (!TryResolveAccount(raw.ToAccount, eligibleAccounts, out _))
            {
                AddField(fields, "toAccount");
            }
            else if (!string.IsNullOrEmpty(userInput)
                && !fields.Contains("toAccount", StringComparer.OrdinalIgnoreCase)
                && !IsAccountMentionedInInput(raw.ToAccount!, userInput))
            {
                AddField(fields, "toAccount");
            }

            if (!string.IsNullOrEmpty(userInput)
                && !fields.Contains("account", StringComparer.OrdinalIgnoreCase)
                && !IsAccountMentionedInInput(raw.Account!, userInput))
            {
                AddField(fields, "account");
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

        return raw.Intent switch
        {
            IntentSetAside => BuildSetAsideReady(raw, eligibleAccounts, scopeId),
            IntentEvent => BuildEventReady(raw, eligibleAccounts, scopeId),
            _ => BuildTransactionReady(raw, eligibleAccounts, scopeId)
        };
    }

    private static InterpretResponse BuildSetAsideReady(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        if (!TryResolveAccount(raw.Account, eligibleAccounts, out var accountId))
        {
            return NotFound(raw, raw.Account, eligibleAccounts);
        }

        var preview = new InterpretSetAsideData
        {
            Name = raw.SetAsideName!,
            Amount = raw.Amount!.Value,
            Account = raw.Account
        };

        var command = new CreateSetAsideCommand
        {
            ScopeId = scopeId,
            AccountId = accountId,
            Name = raw.SetAsideName!.Trim(),
            Amount = raw.Amount!.Value,
            Kind = SetAsideKind.Saving
        };

        return new InterpretResponse
        {
            Intent = raw.Intent,
            State = "Ready",
            SetAsidePreview = preview,
            SetAsideCommand = command,
            Clarifications = []
        };
    }

    private static InterpretResponse BuildEventReady(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        if (!TryResolveAccount(raw.Account, eligibleAccounts, out var accountId))
        {
            return NotFound(raw, raw.Account, eligibleAccounts);
        }

        DateOnly? dueDate = string.IsNullOrWhiteSpace(raw.Date) ? null : ParseDate(raw.Date!);
        var isIncome = raw.Direction == "Income";

        var preview = new InterpretEventData
        {
            Title = raw.Title!,
            Amount = raw.Amount!.Value,
            Direction = isIncome ? "Income" : "Expense",
            Account = raw.Account,
            Date = raw.Date
        };

        var command = new CreateUpcomingEventCommand
        {
            ScopeId = scopeId,
            AccountId = accountId,
            Title = raw.Title!.Trim(),
            Amount = raw.Amount!.Value,
            Direction = isIncome ? UpcomingEventDirection.Income : UpcomingEventDirection.Expense,
            DueDate = dueDate,
            ScheduleKind = dueDate.HasValue
                ? UpcomingEventScheduleKind.Scheduled
                : UpcomingEventScheduleKind.Flexible
        };

        return new InterpretResponse
        {
            Intent = raw.Intent,
            State = "Ready",
            EventPreview = preview,
            EventCommand = command,
            Clarifications = []
        };
    }

    private static InterpretResponse BuildTransactionReady(
        InterpretResult raw,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        Guid scopeId)
    {
        if (!TryResolveAccount(raw.Account, eligibleAccounts, out var accountId))
        {
            return NotFound(raw, raw.Account, eligibleAccounts);
        }

        if (raw.TransactionType == "Transfer")
        {
            if (!TryResolveAccount(raw.ToAccount, eligibleAccounts, out var toAccountId))
            {
                return NotFound(raw, raw.ToAccount, eligibleAccounts);
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

        var command = BuildCreateTransactionCommand(raw, accountId, scopeId, eligibleAccounts);

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
        IReadOnlyList<AccountLookup> eligibleAccounts)
    {
        var amount = raw.Amount!.Value;
        var type = raw.TransactionType!;
        var occurredOn = string.IsNullOrWhiteSpace(raw.Date)
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : ParseDate(raw.Date!);

        if (type == "Transfer")
        {
            TryResolveAccount(raw.ToAccount, eligibleAccounts, out var toAccountId);
            var fee = raw.FeeAmount ?? 0m;

            return new CreateTransactionCommand
            {
                ScopeId = scopeId,
                Type = TransactionType.Transfer,
                Amount = amount,
                Description = raw.Description,
                OccurredOn = occurredOn,
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
            OccurredOn = occurredOn,
            Entries =
            [
                new CreateTransactionEntryCommand { AccountId = accountId, Amount = signedAmount }
            ]
        };
    }

    private static InterpretResponse NotFound(
        InterpretResult raw, string? accountName, IReadOnlyList<AccountLookup> eligibleAccounts)
        => new()
        {
            Intent = raw.Intent,
            State = "NeedsClarification",
            Clarifications =
                [$"Account '{accountName}' not found. Available: {string.Join(", ", eligibleAccounts.Select(a => a.Name))}"]
        };

    private static bool TryResolveAccount(
        string? accountName,
        IReadOnlyList<AccountLookup> eligibleAccounts,
        out Guid accountId)
    {
        accountId = Guid.Empty;
        if (string.IsNullOrEmpty(accountName)) return false;

        foreach (var account in eligibleAccounts)
        {
            if (string.Equals(account.Name, accountName, StringComparison.OrdinalIgnoreCase))
            {
                accountId = account.Id;
                return true;
            }
        }
        return false;
    }

    private static bool TryParseDate(string value, out DateOnly date)
        => DateOnly.TryParse(value, out date);

    private static DateOnly ParseDate(string value)
        => DateOnly.TryParse(value, out var date)
            ? date
            : throw new ValidationException($"Invalid date: {value}");

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
