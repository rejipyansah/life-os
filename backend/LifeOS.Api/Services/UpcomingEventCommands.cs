using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

/// <summary>
/// Rencana Pengeluaran/Pemasukan. Rencana TIDAK terikat ke Sumber Dana.
/// Akun hanya dipilih SAAT realizasi (lihat <see cref="RealizeUpcomingEventCommand.AccountId"/>).
/// </summary>
public class CreateUpcomingEventCommand
{
    public Guid ScopeId { get; set; }

    /// <summary>
    /// Tidak dipakai untuk create. Field wire legacy; Rencana tidak terikat Sumber Dana.
    /// </summary>
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

/// <summary>
/// Realisasi Rencana menjadi transaksi nyata.
///
/// AccountId = Sumber Dana tempat uang benar-benar keluar/masuk. WAJIB dipilih
/// saat realizasi — bukan terikat permanen dari saat rencana dibuat.
/// SetAsideId = Dana yang Disisihkan opsional yang digunakan (alokasi).
/// </summary>
public class RealizeUpcomingEventCommand
{
    public Guid ScopeId { get; set; }

    /// <summary>Sumber Dana tempat uang keluar/masuk. Wajib.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Opsional. Dana yang Disisihkan (pos) yang dialokasikan/dilepas.</summary>
    public Guid? SetAsideId { get; set; }

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

    /// <summary>
    /// LEGACY ONLY — hanya untuk data lama yang masih menyimpan akun.
    /// Rencana baru tidak pernah mengisinya. Tidak dipakai untuk perhitungan.
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>LEGACY ONLY. Tidak dipakai untuk perhitungan.</summary>
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
