using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

/// <summary>
/// Append-only history of every change to a <see cref="SetAside"/> reserved amount.
/// The current reserved amount of a SetAside is SUM(SetAsideEntry.Amount).
/// </summary>
public class SetAsideEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(SetAside))]
    public Guid SetAsideId { get; set; }

    public SetAside SetAside { get; set; } = null!;

    [ForeignKey(nameof(Scope))]
    public Guid ScopeId { get; set; }

    public Scope Scope { get; set; } = null!;

    public SetAsideEntryType Type { get; set; }

    /// <summary>Signed delta against the reserved amount.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Transaksi riil yang menyebabkan entry ini (hanya untuk <see cref="SetAsideEntryType.Spent"/>).
    /// </summary>
    [ForeignKey(nameof(Transaction))]
    public Guid? TransactionId { get; set; }

    public Transaction? Transaction { get; set; }

    [MaxLength(512)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
