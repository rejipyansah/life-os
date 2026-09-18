using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

public class Allocation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Scope))]
    public Guid ScopeId { get; set; }

    public Scope Scope { get; set; } = null!;

    [ForeignKey(nameof(Account))]
    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
