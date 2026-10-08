using LifeOS.Api.Models;

namespace LifeOS.Api.Services;

/// <summary>
/// Cycle arithmetic for SetAside target-balance-per-cycle normalization.
///
/// Target dan cycle adalah dua konsep terpisah:
///   - TargetAmount != null  → set-aside punya target saldo (boleh tanpa cycle)
///   - CycleKind != None     → set-aside punya siklus dan dinormalisasi ke target
///                             pada awal setiap cycle.
/// </summary>
public static class SetAsideCycle
{
    public static bool HasCycle(SetAsideCycleKind kind) => kind != SetAsideCycleKind.None;

    /// <summary>Hari pertama cycle yang sedang berjalan untuk <paramref name="today"/>.</summary>
    public static DateOnly CurrentCycleStart(
        DateOnly anchor, SetAsideCycleKind kind, DateOnly today)
    {
        if (!HasCycle(kind)) return anchor;

        // Monthly routines follow calendar months, regardless of the original
        // creation day or legacy anchor date.
        if (kind == SetAsideCycleKind.Monthly)
            return new DateOnly(today.Year, today.Month, 1);

        var start = anchor;
        for (var guard = 0; guard < 4096; guard++)
        {
            if (today <= CycleEnd(start, kind)) return start;
            start = NextCycleStart(start, kind);
        }
        return start;
    }

    /// <summary>Hari terakhir (inklusif) dari cycle yang dimulai di <paramref name="start"/>.</summary>
    public static DateOnly CycleEnd(DateOnly start, SetAsideCycleKind kind) => kind switch
    {
        SetAsideCycleKind.Weekly => start.AddDays(6),
        SetAsideCycleKind.Monthly => new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1),
        SetAsideCycleKind.Quarterly => start.AddMonths(3).AddDays(-1),
        SetAsideCycleKind.SemiAnnual => start.AddMonths(6).AddDays(-1),
        SetAsideCycleKind.Annual => start.AddYears(1).AddDays(-1),
        _ => start
    };

    public static DateOnly NextCycleStart(DateOnly start, SetAsideCycleKind kind) => kind switch
    {
        SetAsideCycleKind.Weekly => start.AddDays(7),
        SetAsideCycleKind.Monthly => new DateOnly(start.Year, start.Month, 1).AddMonths(1),
        SetAsideCycleKind.Quarterly => start.AddMonths(3),
        SetAsideCycleKind.SemiAnnual => start.AddMonths(6),
        SetAsideCycleKind.Annual => start.AddYears(1),
        _ => start
    };

    /// <summary>
    /// True bila cycle yang berjalan sudah lewat dan set-aside menunggu normalisasi.
    /// Penormalan hanya dilakukan saat ada command/write — tidak pernah diam-diam di GET.
    /// </summary>
    public static bool IsRolloverPending(SetAside setAside, DateOnly today)
        => HasCycle(setAside.CycleKind)
            && today > CycleEnd(setAside.CycleAnchorDate, setAside.CycleKind);

    /// <summary>
    /// Cycle window yang berlaku untuk akumulasi "Terpakai".
    /// Set-aside tanpa cycle menghitung sepanjang waktu.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) UsageWindow(SetAside setAside, DateOnly today)
    {
        if (!HasCycle(setAside.CycleKind)) return (null, null);

        var start = setAside.CycleKind == SetAsideCycleKind.Monthly
            ? new DateOnly(today.Year, today.Month, 1)
            : IsRolloverPending(setAside, today)
                ? CurrentCycleStart(setAside.CycleAnchorDate, setAside.CycleKind, today)
                : setAside.CycleAnchorDate;

        return (start, CycleEnd(start, setAside.CycleKind));
    }
}
