using LifeOS.Api.Models;
using LifeOS.Api.Services;

namespace LifeOS.Tests;

public class SetAsideCycleTests
{
    [Theory]
    [InlineData(SetAsideCycleKind.Weekly, 7)]
    [InlineData(SetAsideCycleKind.Monthly, 0)]
    [InlineData(SetAsideCycleKind.Quarterly, 0)]
    [InlineData(SetAsideCycleKind.Annual, 0)]
    public void HasCycle_IsTrueOnlyWhenCycleKindIsNotNone(SetAsideCycleKind kind, int _)
    {
        Assert.Equal(kind != SetAsideCycleKind.None, SetAsideCycle.HasCycle(kind));
    }

    [Fact]
    public void Weekly_CycleWindow_IsSevenDays()
    {
        var start = new DateOnly(2026, 9, 1);

        Assert.Equal(new DateOnly(2026, 9, 7), SetAsideCycle.CycleEnd(start, SetAsideCycleKind.Weekly));
        Assert.Equal(new DateOnly(2026, 9, 8), SetAsideCycle.NextCycleStart(start, SetAsideCycleKind.Weekly));
    }

    [Fact]
    public void Monthly_CycleWindow_CoversWholeMonth()
    {
        var start = new DateOnly(2026, 9, 1);

        Assert.Equal(new DateOnly(2026, 9, 30), SetAsideCycle.CycleEnd(start, SetAsideCycleKind.Monthly));
        Assert.Equal(new DateOnly(2026, 10, 1), SetAsideCycle.NextCycleStart(start, SetAsideCycleKind.Monthly));
    }

    [Fact]
    public void Monthly_CycleEnd_IsInclusiveLastDay()
    {
        var start = new DateOnly(2026, 2, 1);

        Assert.Equal(new DateOnly(2026, 2, 28), SetAsideCycle.CycleEnd(start, SetAsideCycleKind.Monthly));
    }

    [Fact]
    public void CurrentCycleStart_AdvancesToTheWindowContainingToday()
    {
        var anchor = new DateOnly(2026, 9, 1);

        // Masih di dalam cycle September.
        Assert.Equal(new DateOnly(2026, 9, 1),
            SetAsideCycle.CurrentCycleStart(anchor, SetAsideCycleKind.Monthly, new DateOnly(2026, 9, 15)));

        // Sudah masuk Oktober.
        Assert.Equal(new DateOnly(2026, 10, 1),
            SetAsideCycle.CurrentCycleStart(anchor, SetAsideCycleKind.Monthly, new DateOnly(2026, 10, 15)));

        // Beberapa cycle terlewat sekaligus.
        Assert.Equal(new DateOnly(2026, 12, 1),
            SetAsideCycle.CurrentCycleStart(anchor, SetAsideCycleKind.Monthly, new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void IsRolloverPending_TrueExactlyWhenTodayIsPastCycleEnd()
    {
        var setAside = new SetAside
        {
            CycleKind = SetAsideCycleKind.Monthly,
            CycleAnchorDate = new DateOnly(2026, 9, 1)
        };

        Assert.False(SetAsideCycle.IsRolloverPending(setAside, new DateOnly(2026, 9, 30)));
        Assert.True(SetAsideCycle.IsRolloverPending(setAside, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void CycleKind_None_IsNeverPending()
    {
        var setAside = new SetAside
        {
            CycleKind = SetAsideCycleKind.None,
            CycleAnchorDate = new DateOnly(2020, 1, 1)
        };

        Assert.False(SetAsideCycle.IsRolloverPending(setAside, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void UsageWindow_SpansWholeTime_WhenThereIsNoCycle()
    {
        var setAside = new SetAside { CycleKind = SetAsideCycleKind.None };

        var (from, to) = SetAsideCycle.UsageWindow(setAside, new DateOnly(2026, 10, 1));

        Assert.Null(from);
        Assert.Null(to);
    }

    [Fact]
    public void UsageWindow_IsTheCurrentCycle_WhenThereIsACycle()
    {
        var setAside = new SetAside
        {
            CycleKind = SetAsideCycleKind.Monthly,
            CycleAnchorDate = new DateOnly(2026, 9, 1)
        };

        var (from, to) = SetAsideCycle.UsageWindow(setAside, new DateOnly(2026, 9, 15));

        Assert.Equal(new DateOnly(2026, 9, 1), from);
        Assert.Equal(new DateOnly(2026, 9, 30), to);
    }
}
