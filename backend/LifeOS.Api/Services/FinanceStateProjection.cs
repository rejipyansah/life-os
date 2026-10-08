using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

/// <summary>
/// Read model halaman Finance.
///
/// Semua nilai adalah DERIVED STATE. Tidak ada satu pun yang disimpan sebagai
/// source of truth — tidak ada Account.FreeBalance, tidak ada Finance.UangBebas,
/// tidak ada Account.AvailableBalance.
///
/// DUA ANGKA TERPISAH (jangan disamakan):
///   TotalAvailable        = TotalActualBalance − TotalSetAside
///     Uang yang belum dialokasikan ke Dana yang Disisihkan (sebelum komitmen Rencana).
///   FreeCash (Uang Bebas) = TotalActualBalance − TotalSetAside
///                           + PendingCycleSurplus − ScheduledExpenseCommitments
///     Kekurangan target cycle yang belum didanai tidak mengurangi FreeCash.
///     Uang yang benar-benar bebas dibelanjakan setelah semua komitmen.
///
/// SetAside tidak terikat Sumber Dana — total dihitung scope-wide.
/// SetAside.AccountId legacy TIDAK dipakai untuk perhitungan apa pun.
///
/// Formula:
///   TotalActualBalance           = Σ actual balance akun            (= Σ TransactionEntry.Amount)
///   TotalSetAside                = Σ saldo disisihkan aktif          (= Σ SetAsideEntry.Amount, scope-wide)
///   PendingCycleFunding          = Σ kebutuhan pendanaan cycle (informasi; bukan komitmen FreeCash)
///   PendingCycleSurplus          = Σ surplus cycle yang akan dikembalikan  (scope-wide)
///   TotalCommittedSetAside       = TotalSetAside − PendingCycleSurplus
///   TotalAvailable               = TotalActualBalance − TotalSetAside
///   DueObligations               = Σ agenda pengeluaran terjadwal yang jatuh tempo/sudah lewat
///   ScheduledExpenseCommitments  = Σ SEMUA agenda pengeluaran terjadwal (sekali jalan & ber-siklus)
///                                   — dikurangi dari FreeCash sejak dibuat,
///                                   mirip komitmen Pos, tanpa membuat transaksi.
///   FreeCash (Uang Bebas)        = TotalActualBalance - TotalCommittedSetAside
///                                   - ScheduledExpenseCommitments
///
/// FreeCash tidak pernah di-clamp ke 0. Nilai negatif adalah kondisi nyata yang harus
/// ditampilkan, bukan disembunyikan.
///
/// Catatan: agenda yang sedang jatuh tempo TIDAK dihitung dua kali —
/// komitmennya sudah masuk ScheduledExpenseCommitments sejak dibuat.
/// DueObligations tetap mencakup semua tagihan jatuh tempo untuk tampilan Jatuh Tempo.
/// </summary>
public class FinanceStateProjection
{
    public DateOnly Today { get; set; }

    public decimal TotalActualBalance { get; set; }
    public decimal TotalSetAside { get; set; }
    public decimal PendingCycleFunding { get; set; }
    public decimal PendingCycleSurplus { get; set; }
    public decimal TotalCommittedSetAside { get; set; }

    /// <summary>
    /// TotalAvailable = TotalActual − TotalSetAside.
    /// Uang yang belum dialokasikan ke Dana yang Disisihkan.
    /// BERBEDA dari FreeCash (yang juga mengurangi komitmen Rencana).
    /// </summary>
    public decimal TotalAvailable { get; set; }

    public decimal DueObligations { get; set; }
    public int DueObligationsCount { get; set; }
    public int OverdueObligationsCount { get; set; }

    /// <summary>
    /// Komitmen agenda pengeluaran terjadwal (sekali jalan & ber-siklus) untuk cycle berjalan.
    /// Mengurangi FreeCash tanpa menjadi transaksi (mirip disisihkan).
    /// </summary>
    public decimal ScheduledExpenseCommitments { get; set; }

    /// <summary>
    /// FreeCash (Uang Bebas) = TotalActual − TotalSetAside + PendingCycleSurplus
    ///                          − ScheduledExpenseCommitments.
    /// Derived, tidak disimpan, tidak pernah di-clamp. BERBEDA dari TotalAvailable.
    /// </summary>
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

    /// <summary>Saldo riil. SUM(TransactionEntry.Amount). Lokasi uang di akun ini.</summary>
    public decimal ActualBalance { get; set; }

    /// <summary>
    /// SELALU 0 — alokasi (Dana yang Disisihkan) scope-wide, bukan milik akun tertentu.
    /// SetAside.AccountId legacy tidak dipakai untuk mengurangi saldo per akun.
    /// </summary>
    public decimal SetAsideAmount { get; set; }

    /// <summary>
    /// Saldo aktual akun ini. Uang di akun ini bisa dipakai dari akun ini.
    /// Alokasi dihitung di TotalAvailable scope-wide, bukan per akun.
    /// </summary>
    public decimal AvailableBalance { get; set; }

    public DateTime CreatedAt { get; set; }
}
