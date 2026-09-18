using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

public class Transaction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Scope))]
    public Guid ScopeId { get; set; }

    public Scope Scope { get; set; } = null!;

    public TransactionType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(128)]
    public string? CategoryName { get; set; }

    public DateOnly OccurredOn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(RelatedTransaction))]
    public Guid? RelatedTransactionId { get; set; }

    public Transaction? RelatedTransaction { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? FeeAmount { get; set; }
}
