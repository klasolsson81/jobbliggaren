using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Time;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Invariants of <see cref="CivilMonth"/> — the Swedish civil month LABEL
/// introduced so the two month-windowed surfaces stop writing calendar
/// arithmetic by hand (CTO-bind 2026-07-28-B).
///
/// <para>
/// The rollover is what earns the type. Before it, the activity report and the
/// statistics calculator needed three hand-written December-to-January steps
/// between them, in two files that own no calendar rule. Everything below is a
/// property of the LABEL only: no time zone, no instant, nothing DST can reach.
/// </para>
/// </summary>
public class CivilMonthTests
{
    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 12)]
    [InlineData(1, 1)]
    [InlineData(9999, 12)]
    public void Of_RoundTripsItsArguments(int year, int month)
    {
        var civilMonth = CivilMonth.Of(year, month);

        civilMonth.Year.ShouldBe(year);
        civilMonth.Month.ShouldBe(month);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2026, -1)]
    [InlineData(0, 6)]
    [InlineData(-1, 6)]
    [InlineData(10000, 6)]
    public void Of_FailsLoud_OutsideTheCalendar(int year, int month)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CivilMonth.Of(year, month));
    }

    [Fact]
    public void Of_DoesNotEnforceThePolicyRange()
    {
        // The 2000-2100 bound belongs to GetActivityReportQueryValidator, which
        // states it as a wire-validation rule with its own Swedish message.
        // Duplicating it here would give one rule two homes and two chances to
        // drift. Pinned so a reviewer reads the absence as deliberate.
        Should.NotThrow(() => CivilMonth.Of(1999, 6));
        Should.NotThrow(() => CivilMonth.Of(2101, 6));
    }

    [Fact]
    public void Next_RollsTheYear_AtDecember()
    {
        var december = CivilMonth.Of(2026, 12);

        var january = december.Next();

        january.Year.ShouldBe(2027);
        january.Month.ShouldBe(1);
    }

    [Fact]
    public void Previous_RollsTheYearBack_AtJanuary()
    {
        var january = CivilMonth.Of(2026, 1);

        var december = january.Previous();

        december.Year.ShouldBe(2025);
        december.Month.ShouldBe(12);
    }

    [Fact]
    public void NextAndPrevious_AreInverses_AcrossTheYearBoundary()
    {
        // The two directions are not symmetric for free: they are separate index
        // arithmetic, and an off-by-one in either shows up only here.
        var december = CivilMonth.Of(2026, 12);

        december.Next().Previous().ShouldBe(december);
        december.Previous().Next().ShouldBe(december);
    }

    [Fact]
    public void Previous_TwelveTimes_LandsOnTheSameMonthOneYearEarlier()
    {
        // The exact walk ApplicationStatsCalculator does to reach the oldest
        // bucket of its rolling 12-month series, minus one step.
        var month = CivilMonth.Of(2026, 6);

        for (var i = 0; i < 12; i++)
            month = month.Previous();

        month.ShouldBe(CivilMonth.Of(2025, 6));
    }

    [Fact]
    public void EqualityIsByValue()
    {
        CivilMonth.Of(2026, 7).ShouldBe(CivilMonth.Of(2026, 7));
        CivilMonth.Of(2026, 7).ShouldNotBe(CivilMonth.Of(2026, 8));
        CivilMonth.Of(2026, 7).ShouldNotBe(CivilMonth.Of(2025, 7));
    }

    [Fact]
    public void Default_IsOutsideTheCalendar_AndFailsClosedAtTheFirstOperation()
    {
        // A struct's `default` always bypasses the factory — that is unavoidable,
        // not an oversight, and the type's doc says so. What matters is that it
        // cannot silently produce a wrong month: year 0 is not a calendar year,
        // and the first step off it throws. The throw comes from Next() routing
        // back through Of(), BEFORE the adapter's DateTime construction is
        // reached — an earlier draft of the doc named the adapter as the guard,
        // which is one layer too late.
        default(CivilMonth).Year.ShouldBe(0);

        Should.Throw<ArgumentOutOfRangeException>(() => default(CivilMonth).Next());
        Should.Throw<ArgumentOutOfRangeException>(() => new SwedishCalendar().MonthWindow(default));
    }

    [Fact]
    public void Next_AtTheEndOfTheCalendar_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CivilMonth.Of(9999, 12).Next());
    }

    [Fact]
    public void Previous_AtTheStartOfTheCalendar_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CivilMonth.Of(1, 1).Previous());
    }

    [Fact]
    public void ToString_IsTheSortableCivilForm()
    {
        CivilMonth.Of(2026, 7).ToString().ShouldBe("2026-07");
        CivilMonth.Of(2026, 12).ToString().ShouldBe("2026-12");
    }
}
