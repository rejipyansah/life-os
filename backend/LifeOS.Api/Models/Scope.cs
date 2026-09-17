using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace LifeOS.Api.Models;

public class Scope
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public ScopeType Type { get; set; }

    [ForeignKey(nameof(OwnerUser))]
    public string? OwnerUserId { get; set; }

    public IdentityUser? OwnerUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
