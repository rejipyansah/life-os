using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public class CreateTransactionCommand
{
    public Guid ScopeId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public DateOnly OccurredOn { get; set; }
    public Guid? RelatedTransactionId { get; set; }
    public decimal? FeeAmount { get; set; }
    public List<CreateTransactionEntryCommand> Entries { get; set; } = [];
}

public class CreateTransactionEntryCommand
{
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// Membatalkan transaksi yang sudah diposting.
/// Transaksi asli tidak diubah; yang dibuat adalah Reversal berlawanan arah.
/// </summary>
public class ReverseTransactionCommand
{
    public Guid ScopeId { get; set; }
    public Guid TransactionId { get; set; }

    /// <summary>Alasan pembatalan — disimpan sebagai Description pada transaksi Reversal.</summary>
    public string? Reason { get; set; }

    public DateOnly? OccurredOn { get; set; }
}
