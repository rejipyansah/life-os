using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

/// <summary>
/// Read model halaman Finance.
///
/// Semua nilai adalah DERIVED STATE. Tidak ada satu pun yang disimpan sebagai
/// source of truth — tidak ada Account.FreeBalance, tidak ada Finance.UangBebas,
/// tidak ada Account.AvailableBalance.
///
/// Formula:
///   TotalActualBalance     = Σ actual balance akun            (= Σ TransactionEntry.Amount)
///   TotalSetAside          = Σ saldo disisihkan aktif          (= Σ SetAsideEntry.Amount)
///   PendingCycleFunding    = Σ funding yang dibutuhkan cycle yang belum dinormalisasi
///   PendingCycleSurplus    = Σ surplus yang akan dikembalikan ke Uang Bebas
///   TotalCommittedSetAside = TotalSetAside + PendingCycleFunding - PendingCycleSurplus
///   DueObligations         = Σ agenda pengeluaran terjadwal yang jatuh tempo/sudah lewat
///   FreeCash (Uang Bebas)  = TotalActualBalance - TotalCommittedSetAside - DueObligations
///
/// FreeCash tidak pernah di-clamp ke 0. Nilai negatif adalah kondisi nyata yang harus
/// ditampilkan, bukan disembunyikan.
/// </summary>
public class FinanceStateProjection
{
    public DateOnly Today { get; set; }

    public decimal TotalActualBalance { get; set; }
    public decimal TotalSetAside { get; set; }
    public decimal PendingCycleFunding { get; set; }
    public decimal PendingCycleSurplus { get; set; }
    public decimal TotalCommittedSetAside { get; set; }
    public decimal TotalAvailable { get; set; }

    public decimal DueObligations { get; set; }
    public int DueObligationsCount { get; set; }
    public int OverdueObligationsCount { get; set; }

    /// <summary>Uang Bebas. Derived, tidak disimpan, tidak pernah di-clamp.</summary>
    public decimal FreeCash { get; set; }

    public bool HasUnpaidBills { get; set; }
    public bool AllBillsPaid { get; set; }

    public List<AccountStateProjection> Accounts { get; set; } = [];
    public List<SetAsideProjection> SetAsides { get; set; } = [];
    public List<UpcomingEventProjection> UpcomingEvents { get; set; } = [];
    public List<UpcomingEventProjection> DueEvents { get; set; } = [];
    public List<TransactionProjection> RecentTransactions { get; set; } = [];
}

public class AccountStateProjection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>Saldo riil. SUM(TransactionEntry.Amount).</summary>
    public decimal ActualBalance { get; set; }

    /// <summary>Uang yang sedang disisihkan pada akun ini.</summary>
    public decimal SetAsideAmount { get; set; }

    /// <summary>Saldo tersedia = ActualBalance - SetAsideAmount.</summary>
    public decimal AvailableBalance { get; set; }

    public decimal PendingCycleFunding { get; set; }
    public decimal PendingCycleSurplus { get; set; }

    public DateTime CreatedAt { get; set; }
}
