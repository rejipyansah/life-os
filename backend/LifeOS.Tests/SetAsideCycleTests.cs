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
        Assert.Equal(
            new DateOnly(2028, 2, 29),
            SetAsideCycle.CycleEnd(new DateOnly(2028, 2, 1), SetAsideCycleKind.Monthly));
    }

    [Fact]
    public void Monthly_CycleAlwaysUsesCalendarMonth_RegardlessOfCreationDate()
    {
        var createdOn = new DateOnly(2026, 10, 8);

        Assert.Equal(new DateOnly(2026, 10, 1),
            SetAsideCycle.CurrentCycleStart(createdOn, SetAsideCycleKind.Monthly, createdOn));
        Assert.Equal(new DateOnly(2026, 10, 31),
            SetAsideCycle.CycleEnd(createdOn, SetAsideCycleKind.Monthly));
        Assert.Equal(new DateOnly(2026, 11, 1),
            SetAsideCycle.NextCycleStart(createdOn, SetAsideCycleKind.Monthly));
        Assert.Equal(new DateOnly(2026, 3, 1),
            SetAsideCycle.CurrentCycleStart(
                new DateOnly(2026, 1, 31), SetAsideCycleKind.Monthly, new DateOnly(2026, 3, 12)));
    }

    [Fact]
    public void BusinessDate_UsesWibMidnightForCycleWindows()
    {
        Assert.Equal(
            new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc),
            BusinessDate.StartOfDayUtc(new DateOnly(2026, 10, 1)));
        Assert.Equal(
            DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
            BusinessDate.TodayWib);
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

    [Fact]
    public void UsageWindow_MonthlyUsesCalendarMonthEvenForLegacyMidMonthAnchor()
    {
        var setAside = new SetAside
        {
            CycleKind = SetAsideCycleKind.Monthly,
            CycleAnchorDate = new DateOnly(2026, 10, 8)
        };

        var (from, to) = SetAsideCycle.UsageWindow(setAside, new DateOnly(2026, 10, 2));

        Assert.Equal(new DateOnly(2026, 10, 1), from);
        Assert.Equal(new DateOnly(2026, 10, 31), to);
    }
}
