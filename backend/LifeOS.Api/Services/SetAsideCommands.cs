using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

/// <summary>
/// Membuat Dana yang Disisihkan (pos alokasi).
///
/// SourceAccountId = referensi rekening yang biasa dipakai untuk proses manual.
/// - Tidak membatasi nominal dan tidak didebit.
/// - Nilai ini DISIMPAN sebagai hint non-binding (DefaultSourceAccountId) untuk
///   pre-select UI pada proses manual — BUKAN kepemilikan pos, BUKAN ikatan permanen.
/// - Tidak dipakai untuk perhitungan saldo/available/funding/aloikasi apa pun.
/// </summary>
public class CreateSetAsideCommand
{
    public Guid ScopeId { get; set; }

    /// <summary>
    /// Sumber Dana default untuk proses manual (top-up/pakai).
    /// Divalidasi hanya scope/status; disimpan sebagai hint non-binding.
    /// </summary>
    public Guid? SourceAccountId { get; set; }

    public string Name { get; set; } = "";
    public SetAsideKind Kind { get; set; } = SetAsideKind.Saving;
    public string? Note { get; set; }

    /// <summary>Target saldo. Null = tanpa target (fleksibel).</summary>
    public decimal? TargetAmount { get; set; }

    /// <summary>Target saldo per siklus hanya berlaku bila != None.</summary>
    public SetAsideCycleKind CycleKind { get; set; } = SetAsideCycleKind.None;

    /// <summary>Saldo yang langsung disisihkan saat dibuat. Boleh 0.</summary>
    public decimal Amount { get; set; }
}

public class UpdateSetAsideCommand
{
    public Guid ScopeId { get; set; }
    public string? Name { get; set; }
    public string? Note { get; set; }
    public decimal? TargetAmount { get; set; }
    public bool RemoveTarget { get; set; }
    public SetAsideCycleKind? CycleKind { get; set; }
}

/// <summary>
/// Top-up: menambah saldo yang disisihkan dari Uang Bebas scope-wide, bukan Expense.
/// SourceAccountId hanya referensi untuk pre-select preferensi rekening — tidak didebit.
/// </summary>
public class AddToSetAsideCommand
{
    public Guid ScopeId { get; set; }

    /// <summary>Opsional. Rekening referensi; tidak membatasi saldo atau didebit.</summary>
    public Guid? SourceAccountId { get; set; }

    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

public class WithdrawFromSetAsideCommand
{
    public Guid ScopeId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Real spending drawn from a set-aside: creates an Expense Transaction AND releases the
/// reserved amount.
///
/// SourceAccountId = Sumber Dana tempat uang BENAR-BENAR keluar. Wajib.
/// Seluruh nominal keluar dari SourceAccountId (validasi saldo aktual akun itu).
/// Porsi di atas saldo pos (shortfall) ditanggung uang yang belum dialokasikan
/// (Uang Bebas scope-wide) — pos tidak terikat ke akun manapun.
/// </summary>
public class SpendFromSetAsideCommand
{
    public Guid ScopeId { get; set; }

    /// <summary>Sumber Dana tempat uang keluar. Wajib.</summary>
    public Guid SourceAccountId { get; set; }

    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string? Note { get; set; }
}

public class CloseSetAsideCommand
{
    public Guid ScopeId { get; set; }
    public SetAsideCloseReason Reason { get; set; } = SetAsideCloseReason.Cancelled;
    public string? Note { get; set; }
}

public class SetAsideProjection
{
    public Guid Id { get; set; }

    /// <summary>
    /// LEGACY ONLY — alokasi tidak terikat Sumber Dana; field ini tidak pernah
    /// dipakai untuk perhitungan. Nilai legacy dibiarkan null untuk pos baru.
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>LEGACY ONLY. Tidak dipakai untuk perhitungan.</summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// Hint non-binding: sumber dana default untuk proses manual (top-up/pakai).
    /// Hanya pre-select UI — bukan ikatan, bukan validasi.
    /// </summary>
    public Guid? DefaultSourceAccountId { get; set; }

    /// <summary>Nama sumber dana default untuk tampilan.</summary>
    public string? DefaultSourceAccountName { get; set; }

    public string Name { get; set; } = "";
    public SetAsideKind? Kind { get; set; }
    public string? Note { get; set; }

    /// <summary>Saldo yang sedang disisihkan (SUM dari history).</summary>
    public decimal Amount { get; set; }

    public decimal? TargetAmount { get; set; }

    /// <summary>
    /// Jarak ke target saat ini: max(0, TargetAmount - Amount).
    /// Tetap terlihat setelah cycle dinormalisasi, termasuk saat pendanaan tidak cukup.
    /// </summary>
    public decimal TargetShortfall { get; set; }

    public SetAsideCycleKind CycleKind { get; set; }
    public DateOnly CycleAnchorDate { get; set; }

    public DateOnly? CurrentCycleStart { get; set; }
    public DateOnly? CurrentCycleEnd { get; set; }
    public bool IsCycleRolloverPending { get; set; }
    public decimal CycleFundingRequired { get; set; }
    public decimal CycleSurplus { get; set; }
    public decimal CycleFundingShortfall { get; set; }
    public bool IsUnderfunded { get; set; }

    /// <summary>True bila RoutineBatch sudah direalisasikan pada cycle saat ini.</summary>
    public bool IsCycleExecuted { get; set; }

    /// <summary>
    /// Total pemakaian pada cycle berjalan (atau sepanjang waktu bila tanpa cycle),
    /// termasuk porsi yang ditutup uang bebas — nominal penuh transaksinya,
    /// bukan hanya yang keluar dari saldo pos.
    /// </summary>
    public decimal UsedAmount { get; set; }

    public SetAsideStatus Status { get; set; }
    public SetAsideCloseReason? CloseReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SetAsideEntryProjection> RecentEntries { get; set; } = [];
}

public class SetAsideEntryProjection
{
    public Guid Id { get; set; }
    public SetAsideEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public Guid? TransactionId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal BalanceAfter { get; set; }
    public SetAsideTransactionSummary? Transaction { get; set; }
}

public class SetAsideTransactionSummary
{
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string? RelatedDescription { get; set; }
}

public class SetAsideHistoryPage
{
    public List<SetAsideEntryProjection> Items { get; set; } = [];
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}

public class SetAsideOperationResult
{
    public SetAsideProjection SetAside { get; set; } = null!;
    public SetAsideEntryProjection Entry { get; set; } = null!;

    /// <summary>Transaksi Expense yang tercipta; hanya untuk operasi Spend.</summary>
    public Guid? TransactionId { get; set; }

    /// <summary>Bagian cycle funding yang tidak terpenuhi karena uang bebas tidak cukup.</summary>
    public decimal CycleFundingShortfall { get; set; }
}
