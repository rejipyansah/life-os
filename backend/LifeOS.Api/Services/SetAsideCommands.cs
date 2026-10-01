using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

public class CreateSetAsideCommand
{
    public Guid ScopeId { get; set; }
    public Guid AccountId { get; set; }
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

    /// <summary>Memindahkan set-aside ke akun lain dalam scope yang sama.</summary>
    public Guid? AccountId { get; set; }
}

public class AddToSetAsideCommand
{
    public Guid ScopeId { get; set; }
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
/// reserved amount. Overspend beyond the reserved amount is borne by available money
/// (Uang Bebas) — optionally from a user-selected source account.
/// </summary>
public class SpendFromSetAsideCommand
{
    public Guid ScopeId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public DateOnly OccurredOn { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Rekening sumber Uang Bebas untuk porsi shortfall (di atas saldo SetAside).
    /// Null = ambil dari akun SetAside itu sendiri.
    /// </summary>
    public Guid? FreeCashAccountId { get; set; }
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
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = "";
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

    /// <summary>
    /// Total pemakaian pada cycle berjalan (atau sepanjang waktu bila tanpa cycle),
    /// termasuk porsi yang ditutup Uang Bebas — nominal penuh transaksinya,
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
}

public class SetAsideOperationResult
{
    public SetAsideProjection SetAside { get; set; } = null!;
    public SetAsideEntryProjection Entry { get; set; } = null!;

    /// <summary>Transaksi Expense yang tercipta; hanya untuk operasi Spend.</summary>
    public Guid? TransactionId { get; set; }

    /// <summary>Bagian cycle funding yang tidak terpenuhi karena Uang Bebas tidak cukup.</summary>
    public decimal CycleFundingShortfall { get; set; }
}
