using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeOS.Api.Models;

/// <summary>
/// Uang yang Disisihkan (set-aside / pos alokasi) — alokasi/tujuan uang.
///
/// SEBUAH POOL ALOKASI SCOPE-WIDE, bukan milik satu Sumber Dana:
///   - tidak pernah membuat TransactionEntry
///   - tidak pernah mengubah saldo aktual akun manapun
///   - hanya mengurangi derived TotalAvailable / FreeCash
///
/// Saldo saat ini diturunkan dari <see cref="SetAsideEntry"/> history (SUM).
///
/// LEGACY: kolom <see cref="AccountId"/> boleh berisi nilai lama dari data sebelum
/// pemisahan konsep. Nilai legacy TIDAK PERNAH dipakai untuk perhitungan saldo,
/// uang yang bisa dipakai, pendanaan cycle, maupun alokasi. SetAside baru tidak pernah
/// menulis kolom ini. Sumber Dana hanya ditentukan pada transaksi/perpindahan uang,
/// bukan pada Dana yang Disisihkan.
///
/// <see cref="DefaultSourceAccountId"/> adalah HINT NON-BINDING: sumber dana default
/// yang direkomendasikan untuk proses manual (top-up/pakai/eksekusi). Hanya dipakai
/// untuk pre-select di UI — TIDAK PERNAH dipakai untuk validasi/perhitungan apa pun,
/// dan bukan kepemilikan pos.
/// </summary>
public class SetAside
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Scope))]
    public Guid ScopeId { get; set; }

    public Scope Scope { get; set; } = null!;

    /// <summary>
    /// LEGACY ONLY — jangan dibaca untuk validasi/perhitungan apa pun.
    /// Tidak ditulis untuk SetAside baru.
    /// </summary>
    [ForeignKey(nameof(Account))]
    public Guid? AccountId { get; set; }

    /// <summary>LEGACY ONLY — tidak pernah dipakai di logic.</summary>
    public Account? Account { get; set; }

    /// <summary>
    /// Hint non-binding: sumber dana default untuk proses manual (top-up/pakai).
    /// Dipakai untuk pre-select UI saja — bukan ikatan, bukan validasi.
    /// </summary>
    [ForeignKey(nameof(DefaultSourceAccount))]
    public Guid? DefaultSourceAccountId { get; set; }

    /// <summary>LEGACY/non-critical navigation untuk hint default.</summary>
    public Account? DefaultSourceAccount { get; set; }

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

    /// <summary>Default category used when recording a real expense from this set-aside.</summary>
    [MaxLength(128)]
    public string? TransactionCategory { get; set; }

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

    /// <summary>
    /// Dana target cycle berjalan yang belum dapat dialokasikan karena Uang Bebas
    /// belum cukup. Akan dipenuhi otomatis ketika ada pemasukan baru.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal CycleFundingShortfall { get; set; }

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
