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

    /// <summary>
    /// Opsional. Dana yang Disisihkan (pos) yang dialokasikan/dilepas pada transaksi ini.
    /// INDEPENDEN dari Sumber Dana (AccountId di Entries) — dua pilihan berbeda:
    ///   Entries[].AccountId = Sumber Dana (uang keluar/masuk dari mana)
    ///   SetAsideId          = alokasi/tujuan uang yang digunakan
    /// Untuk Expense: melepas min(amount, saldoPos) dari pos.
    /// Untuk Income: menambah amount ke pos.
    /// </summary>
    public Guid? SetAsideId { get; set; }

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
