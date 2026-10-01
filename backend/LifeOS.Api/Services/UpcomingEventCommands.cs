using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public class CreateUpcomingEventCommand
{
    public Guid ScopeId { get; set; }
    public Guid? AccountId { get; set; }
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public UpcomingEventDirection Direction { get; set; } = UpcomingEventDirection.Expense;
    public string? CategoryName { get; set; }
    public string? Note { get; set; }
    public DateOnly? DueDate { get; set; }
    public UpcomingEventScheduleKind ScheduleKind { get; set; } = UpcomingEventScheduleKind.Scheduled;
    public UpcomingEventRecurrence Recurrence { get; set; } = UpcomingEventRecurrence.None;
}

public class UpdateUpcomingEventCommand
{
    public Guid ScopeId { get; set; }
    public Guid? AccountId { get; set; }
    public bool ClearAccount { get; set; }
    public string? Title { get; set; }
    public decimal? Amount { get; set; }
    public UpcomingEventDirection? Direction { get; set; }
    public string? CategoryName { get; set; }
    public string? Note { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool ClearDueDate { get; set; }
    public UpcomingEventScheduleKind? ScheduleKind { get; set; }
    public UpcomingEventRecurrence? Recurrence { get; set; }
}

/// <summary>Tanpa NewDueDate, agenda digeser satu hari dari tanggal terakhir yang diketahui.</summary>
public class PostponeUpcomingEventCommand
{
    public Guid ScopeId { get; set; }
    public DateOnly? NewDueDate { get; set; }
    public string? Reason { get; set; }
}

public class SettleUpcomingEventCommand
{
    public Guid ScopeId { get; set; }
    public string? Reason { get; set; }
}

public class RealizeUpcomingEventCommand
{
    public Guid ScopeId { get; set; }

    /// <summary>Tanggal kejadian riil. Default hari ini.</summary>
    public DateOnly? OccurredOn { get; set; }

    /// <summary>
    /// Alasan/label untuk Transaction yang tercipta.
    /// Default memakai judul agenda.
    /// </summary>
    public string? Description { get; set; }
}

public class UpcomingEventProjection
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public UpcomingEventDirection Direction { get; set; }
    public string? CategoryName { get; set; }
    public string? Note { get; set; }
    public DateOnly? DueDate { get; set; }
    public UpcomingEventScheduleKind ScheduleKind { get; set; }
    public UpcomingEventRecurrence Recurrence { get; set; }
    public UpcomingEventStatus Status { get; set; }
    public Guid? RealizedTransactionId { get; set; }
    public string? StatusReason { get; set; }
    public bool IsDue { get; set; }
    public bool IsOverdue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RealizeUpcomingEventResult
{
    public UpcomingEventProjection Event { get; set; } = null!;
    public Guid TransactionId { get; set; }
}
