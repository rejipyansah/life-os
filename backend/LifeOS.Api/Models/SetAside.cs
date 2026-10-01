using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

/// <summary>
/// Uang yang Disisihkan (set-aside / reservation).
/// A reservation of money held inside an Account. It is NOT an expense, it creates no
/// TransactionEntry and never changes the actual account balance. It only reduces the
/// derived available balance.
/// The current reserved amount is derived from <see cref="SetAsideEntry"/> history.
/// </summary>
public class SetAside
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

    /// <summary>
    /// Klasifikasi pos. Nullable karena data legacy dimigrasikan tanpa menebak nilai —
    /// row yang dibuat sebelum konsep ini ada dibiarkan NULL. SetAside baru selalu
    /// diklasifikasikan saat dibuat.
    /// </summary>
    public SetAsideKind? Kind { get; set; } = SetAsideKind.Saving;

    [MaxLength(512)]
    public string? Note { get; set; }

    /// <summary>Target saldo. Nullable: not every set-aside needs a target.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? TargetAmount { get; set; }

    /// <summary>
    /// Target saldo per siklus hanya berlaku bila <see cref="CycleKind"/> != None.
    /// Target dan cycle adalah dua konsep terpisah.
    /// </summary>
    public SetAsideCycleKind CycleKind { get; set; } = SetAsideCycleKind.None;

    /// <summary>Hari mulai cycle yang sedang berjalan.</summary>
    public DateOnly CycleAnchorDate { get; set; }

    public SetAsideStatus Status { get; set; } = SetAsideStatus.Active;

    /// <summary>Alasan penutupan; hanya terisi bila <see cref="Status"/> == Closed.</summary>
    public SetAsideCloseReason? CloseReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum SetAsideCloseReason
{
    Spent = 0,
    Withdrawn = 1,
    Cancelled = 2
}
