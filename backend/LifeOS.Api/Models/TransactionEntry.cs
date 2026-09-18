using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

public class TransactionEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Transaction))]
    public Guid TransactionId { get; set; }

    public Transaction Transaction { get; set; } = null!;

    [ForeignKey(nameof(Account))]
    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
}
