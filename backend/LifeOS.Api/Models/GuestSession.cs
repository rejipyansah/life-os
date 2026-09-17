using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

public class GuestSession
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Scope))]
    public Guid ScopeId { get; set; }

    public Scope Scope { get; set; } = null!;

    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}
