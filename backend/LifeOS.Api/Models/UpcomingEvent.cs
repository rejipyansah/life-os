using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

/// <summary>
/// Agenda Kas Mendatang — an expected/upcoming financial event.
/// It is NOT a Transaction. It never changes the actual balance until it is realized.
/// </summary>
public class UpcomingEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Scope))]
    public Guid ScopeId { get; set; }

    public Scope Scope { get; set; } = null!;

    [ForeignKey(nameof(Account))]
    public Guid? AccountId { get; set; }

    public Account? Account { get; set; }

    [Required]
    [MaxLength(256)]
    public string Title { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public UpcomingEventDirection Direction { get; set; } = UpcomingEventDirection.Expense;

    [MaxLength(128)]
    public string? CategoryName { get; set; }

    [MaxLength(512)]
    public string? Note { get; set; }

    /// <summary>Null bila <see cref="ScheduleKind"/> == Flexible.</summary>
    public DateOnly? DueDate { get; set; }

    public UpcomingEventScheduleKind ScheduleKind { get; set; } = UpcomingEventScheduleKind.Scheduled;

    public UpcomingEventRecurrence Recurrence { get; set; } = UpcomingEventRecurrence.None;

    public UpcomingEventStatus Status { get; set; } = UpcomingEventStatus.Scheduled;

    /// <summary>Transaksi riil yang tercipta saat event direalisasikan.</summary>
    [ForeignKey(nameof(RealizedTransaction))]
    public Guid? RealizedTransactionId { get; set; }

    public Transaction? RealizedTransaction { get; set; }

    [MaxLength(512)]
    public string? StatusReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
