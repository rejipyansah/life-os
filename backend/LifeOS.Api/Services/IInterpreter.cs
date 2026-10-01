using System.Text.Json.Serialization;

namespace LifeOS.Api.Services;

public interface IInterpreter
{
    Task<InterpretResult> InterpretAsync(InterpretRequest request, CancellationToken ct = default);
}

public class InterpretRequest
{
    public required string Input { get; init; }
    public DateOnly CurrentDate { get; init; }
    public required IReadOnlyList<string> EligibleAccounts { get; init; }
}

/// <summary>
/// Mentahan hasil interpreter. Interpreter hanya menafsirkan — ia tidak pernah menjadi
/// source of truth dan tidak pernah menulis ke database.
/// </summary>
public class InterpretResult
{
    /// <summary>CreateTransaction | CreateSetAside | CreateUpcomingEvent | Unsupported</summary>
    public string Intent { get; set; } = "";

    /// <summary>Expense | Income | Transfer — untuk CreateTransaction.</summary>
    public string? TransactionType { get; set; }

    public decimal? Amount { get; set; }
    public string? Description { get; set; }
    public string? Account { get; set; }
    public string? ToAccount { get; set; }
    public string? Date { get; set; }
    public decimal? FeeAmount { get; set; }

    /// <summary>Nama pos untuk CreateSetAside.</summary>
    public string? SetAsideName { get; set; }

    /// <summary>Judul agenda untuk CreateUpcomingEvent.</summary>
    public string? Title { get; set; }

    /// <summary>Income | Expense — arah arus kas untuk CreateUpcomingEvent.</summary>
    public string? Direction { get; set; }

    public List<string> ClarificationFields { get; set; } = [];
}

public class InterpretResponse
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("preview")]
    public InterpretTransactionData? Preview { get; set; }

    [JsonPropertyName("command")]
    public CreateTransactionCommand? Command { get; set; }

    [JsonPropertyName("setAsidePreview")]
    public InterpretSetAsideData? SetAsidePreview { get; set; }

    [JsonPropertyName("setAsideCommand")]
    public CreateSetAsideCommand? SetAsideCommand { get; set; }

    [JsonPropertyName("eventPreview")]
    public InterpretEventData? EventPreview { get; set; }

    [JsonPropertyName("eventCommand")]
    public CreateUpcomingEventCommand? EventCommand { get; set; }

    [JsonPropertyName("clarifications")]
    public List<string> Clarifications { get; set; } = [];
}

public class InterpretTransactionData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("account")]
    public string? Account { get; set; }

    [JsonPropertyName("toAccount")]
    public string? ToAccount { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("feeAmount")]
    public decimal? FeeAmount { get; set; }
}

public class InterpretSetAsideData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("account")]
    public string? Account { get; set; }
}

public class InterpretEventData
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("account")]
    public string? Account { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

public class InterpretInputRequest
{
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";
}

public class InterpreterProviderException : Exception
{
    public InterpreterProviderException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
