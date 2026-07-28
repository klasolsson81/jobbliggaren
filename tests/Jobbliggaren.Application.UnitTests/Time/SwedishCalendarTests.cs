using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Time;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Time;

/// <summary>
/// The gate for <see cref="SwedishCalendar"/>, and specifically for the one
/// thing a doc comment cannot promise: that <c>Europe/Stockholm</c> actually
/// resolves on the runtime the suite is executing on.
///
/// <para>
/// This exists because a probe is not proof. Resolving the id under Windows
/// PowerShell 5.1 fails — but that is .NET Framework, which has no IANA support
/// at all, so the result says nothing about .NET 10. Rather than reason from
/// documentation about what ought to work on a Windows dev machine and on the
/// Linux CI runner, the suite resolves it on both.
/// </para>
///
/// <para>
/// Lives in the Application test project because that is where Infrastructure
/// types are unit-tested here (precedent: <c>Auditing/IpAnonymizerTests</c>);
/// there is no Infrastructure unit-test project.
/// </para>
///
/// <para>
/// <b>One hole is deliberate and cannot usefully be closed:</b> the zone
/// IDENTITY is unpinned. <c>Europe/Berlin</c> or <c>Europe/Oslo</c> would pass
/// every case below, because they share Sweden's CET/CEST rules in all years
/// tested. Asserting <c>ZoneId.ShouldBe("Europe/Stockholm")</c> would be
/// tautological. Recorded so the next reader knows it was considered rather
/// than overlooked.
/// </para>
/// </summary>
public class SwedishCalendarTests
{
    [Fact]
    public void ZoneId_ResolvesOnThisRuntime()
    {
        // Fails loudly on a runtime without ICU/tzdata or with
        // InvariantGlobalization enabled — the two conditions that would break
        // the IANA id. Every other test here would fail too, but reporting a
        // wrong date rather than the cause.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(SwedishCalendar.ZoneId);

        zone.ShouldNotBeNull();
        zone.SupportsDaylightSavingTime.ShouldBeTrue();
    }

    [Theory]
    [InlineData(2026, 1, 15)]   // winter, zone offset +01:00
    [InlineData(2026, 7, 15)]   // summer, zone offset +02:00
    public void StartOfDay_ReturnsAZeroOffsetInstant(int year, int month, int day)
    {
        // THE assertion the rest of this class could not make. Npgsql writes a
        // DateTimeOffset to `timestamptz` ONLY when Offset == 0; a non-zero one
        // throws, and these values are used as query parameters. The repo has
        // been bitten before — PlatsbankenJobSource normalises at the ACL
        // boundary for the same reason, and records that the bug was invisible
        // on a UTC host and fired locally in Sweden at +02:00.
        //
        // Every other assertion here is blind to this: DateTimeOffset.Equals
        // compares the INSTANT only, so `.ToUniversalTime()` before ShouldBe is
        // decorative. Two independent reviewers found the defect that entered
        // through exactly this gap.
        var start = new SwedishCalendar()
            .StartOfDay(new DateTimeOffset(year, month, day, 14, 0, 0, TimeSpan.Zero));

        start.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(2026, 1)]   // winter, zone offset +01:00
    [InlineData(2026, 7)]   // summer, zone offset +02:00
    public void MonthWindow_ReturnsZeroOffsetInstants_AtBOTHEnds(int year, int month)
    {
        // Same contract on the sibling member, so the consumers that arrived in
        // the follow-up cannot inherit the defect.
        //
        // BOTH ends, and `End` is the half that is new. It is a brand-new value
        // used as a `timestamptz` WHERE parameter in
        // GetActivityReportQueryHandler, and Npgsql throws on a non-zero offset.
        // The handler's own unit tests run EF InMemory, which evaluates the
        // predicate in LINQ-to-Objects where offset is irrelevant — so they are
        // blind to this by construction and cannot be the gate (CTO-bind
        // 2026-07-28-B, landing condition 3).
        var window = new SwedishCalendar().MonthWindow(CivilMonth.Of(year, month));

        window.Start.Offset.ShouldBe(TimeSpan.Zero);
        window.End.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(2026, 3, 29)]   // spring forward
    [InlineData(2026, 10, 25)]  // fall back
    public void Midnight_IsNeitherInvalidNorAmbiguous_OnTransitionDates(int year, int month, int day)
    {
        // The port PROMISES callers need no invalid/ambiguous-time handling, and
        // `ToInstant` rests entirely on that. The failure mode is silent:
        // GetUtcOffset(Unspecified) returns the STANDARD offset for both an
        // invalid and an ambiguous local time. If a future tzdata release moved
        // an EU transition to midnight, the calendar would be an hour wrong with
        // every other test still green. This is the only place the promise is
        // checked rather than asserted in prose.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(SwedishCalendar.ZoneId);
        var midnight = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

        zone.IsInvalidTime(midnight).ShouldBeFalse();
        zone.IsAmbiguousTime(midnight).ShouldBeFalse();
    }

    [Fact]
    public void StartOfDay_IgnoresTheInputOffset_ReadingOnlyTheInstant()
    {
        // The port takes any DateTimeOffset. Its only caller passes
        // clock.UtcNow, but the contract does not say so — and that this works
        // today is a property of ConvertTime, not luck. Same instant, three
        // spellings.
        var calendar = new SwedishCalendar();
        var expected = new DateTimeOffset(2026, 5, 22, 22, 0, 0, TimeSpan.Zero);

        calendar.StartOfDay(new DateTimeOffset(2026, 5, 22, 22, 30, 0, TimeSpan.Zero))
            .ShouldBe(expected);
        calendar.StartOfDay(new DateTimeOffset(2026, 5, 23, 0, 30, 0, TimeSpan.FromHours(2)))
            .ShouldBe(expected);
        calendar.StartOfDay(new DateTimeOffset(2026, 5, 22, 18, 30, 0, TimeSpan.FromHours(-4)))
            .ShouldBe(expected);
    }

    [Fact]
    public void StartOfDay_InWinter_IsPreviousDay2300Utc()
    {
        // 15 Jan 2026, Swedish standard time = UTC+1.
        var calendar = new SwedishCalendar();

        var start = calendar.StartOfDay(new DateTimeOffset(2026, 1, 15, 14, 0, 0, TimeSpan.Zero));

        start.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_InSummer_IsPreviousDay2200Utc()
    {
        // 15 Jul 2026, Swedish daylight saving time = UTC+2. Both polarities are
        // pinned because a fixed-offset implementation passes one and fails the
        // other, and the winter case alone would look correct.
        var calendar = new SwedishCalendar();

        var start = calendar.StartOfDay(new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero));

        start.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_JustAfterSwedishMidnight_BelongsToTheNewSwedishDay()
    {
        // 22:30Z on 22 May is 00:30 on 23 May in Sweden (summer, UTC+2). The
        // UTC-derived boundary this replaces would have called this "yesterday"
        // for another 1.5 hours — the defect the directive names.
        var calendar = new SwedishCalendar();

        var start = calendar.StartOfDay(new DateTimeOffset(2026, 5, 22, 22, 30, 0, TimeSpan.Zero));

        start.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 5, 22, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_OnTheSpringForwardDate_StillReturnsMidnight()
    {
        // 29 Mar 2026 is a DST transition date. The transition happens at 01:00
        // UTC (02:00 -> 03:00 local), so MIDNIGHT is neither skipped nor
        // repeated and needs no ambiguity handling. This pins that claim rather
        // than leaving it as a comment: the day still starts at 23:00Z on the
        // 28th, at the pre-transition offset.
        var calendar = new SwedishCalendar();

        var start = calendar.StartOfDay(new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero));

        start.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_OnTheFallBackDate_StillReturnsMidnight()
    {
        // 25 Oct 2026, the other transition. Same reasoning, opposite direction:
        // the day begins at 22:00Z on the 24th, at the pre-transition offset.
        var calendar = new SwedishCalendar();

        var start = calendar.StartOfDay(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

        start.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_IsIdempotent_OnItsOwnResult()
    {
        // The boundary instant belongs to the day it opens, not the one before.
        // An off-by-one in the conversion shows up here and nowhere else.
        var calendar = new SwedishCalendar();
        var start = calendar.StartOfDay(new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero));

        calendar.StartOfDay(start).ShouldBe(start);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void MonthWindow_EndIsTheNextMonthsOwnStart_ForEveryMonthOfTheYear(int month)
    {
        // The contract the type exists to make unrepresentable: the windows TILE
        // the year, no gap and no overlap. Stated as an invariant rather than as
        // twelve literals so it cannot drift with the implementation.
        //
        // ALL TWELVE, deliberately (CTO-bind 2026-07-28-B, landing condition 4).
        // Only five months are broken by the AddMonths derivation; a theory over
        // those five would encode "these months are special" and would pass an
        // implementation that hard-coded them. Twelve cases say nothing about
        // which months are special, which is the point — the invariant is not
        // about DST or month length, it is about contiguity.
        var calendar = new SwedishCalendar();
        var m = CivilMonth.Of(2026, month);

        calendar.MonthWindow(m).End.ShouldBe(calendar.MonthWindow(m.Next()).Start);
    }

    [Fact]
    public void MonthWindow_EndIsNotReproducibleByAddMonths_AndIsSilentlyCorrectInSevenMonthsOfTwelve()
    {
        // THE FORM BOTH REAL CALL SITES USED is the exclusive window END, not a
        // series: `GetActivityReportQueryHandler` and `ApplicationStatsCalculator`
        // both wrote `start.AddMonths(1)` before PR C. (Named by symbol, not by
        // line: an earlier revision of this comment cited `:47` and `:142`, and
        // PR C moved both.) The port's wording once forbade only a "series",
        // which reads as permitting a single AddMonths(1).
        //
        // It is a WHERE against the database, not a label: the activity report
        // silently lost rows, and it is the document a job seeker files with
        // Arbetsförmedlingen, so "quietly too few" is the wrong kind of wrong.
        // Found by test-writer.
        //
        // WHAT THIS TEST ADDS over the contiguity theory above is the measured
        // FAILURE PROFILE, and it is the argument for the whole design: because
        // AddMonths CLAMPS the day-of-month, the derivation is silently correct
        // in SEVEN months of twelve. A hand check in April, June, August,
        // September or November returns green. Pinned by VALUE, and the correct
        // months are pinned too — so nobody "repairs" a month that was never
        // broken, and nobody reads a green spot check as coverage.
        var cal = new SwedishCalendar();
        DateTimeOffset AddMonthsEnd(int m) => cal.MonthWindow(CivilMonth.Of(2026, m)).Start.AddMonths(1);
        DateTimeOffset TrueEnd(int m) => cal.MonthWindow(CivilMonth.Of(2026, m)).End;

        // The five that are wrong. March is the worst, and the cause is MONTH
        // LENGTH: the anchor is 28 February, AddMonths preserves the
        // day-of-month, so it lands on 28 March against a real boundary of the
        // 31st. The spring-forward only moves the boundary HOUR, which is why the
        // gap measures 2 d 23 h rather than a flat three days.
        AddMonthsEnd(3).ShouldBe(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero));
        TrueEnd(3).ShouldBe(new DateTimeOffset(2026, 3, 31, 22, 0, 0, TimeSpan.Zero));
        (TrueEnd(3) - AddMonthsEnd(3)).ShouldBe(new TimeSpan(2, 23, 0, 0));

        (TrueEnd(5) - AddMonthsEnd(5)).ShouldBe(TimeSpan.FromDays(1));
        (TrueEnd(7) - AddMonthsEnd(7)).ShouldBe(TimeSpan.FromDays(1));    // 31 July vanishes
        (TrueEnd(12) - AddMonthsEnd(12)).ShouldBe(TimeSpan.FromDays(1));
        // October carries an extra hour on top of the day, because the fall-back
        // sits between the two boundaries.
        (TrueEnd(10) - AddMonthsEnd(10)).ShouldBe(new TimeSpan(1, 1, 0, 0));

        // The seven that are ACCIDENTALLY RIGHT. February is exact in every year,
        // leap or not — 31 January plus one month clamps to the last day of
        // February, which IS the boundary. This is the half a spot check sees.
        int[] silentlyCorrect = [1, 2, 4, 6, 8, 9, 11];
        foreach (var m in silentlyCorrect)
            AddMonthsEnd(m).ShouldBe(TrueEnd(m));
    }

    [Theory]
    [InlineData(2026, 7, 2026, 6, 30)]   // a July bucket would be labelled JUNE
    [InlineData(2026, 1, 2025, 12, 31)]  // a January bucket: wrong month AND wrong YEAR
    public void MonthWindow_StartIsNotTheFirstOfTheMonth_ButTheWindowCarriesTheRightLabel(
        int year, int month, int expectedStartYear, int expectedStartMonth, int expectedStartDay)
    {
        // The misleading source and the correct one, asserted SIDE BY SIDE — this
        // is what makes the design executable rather than merely documented
        // (CTO-bind 2026-07-28-B, landing condition 4).
        //
        // `ApplicationStatsCalculator` used to build
        // `new MonthlyApplicationCountDto(monthStart.Year, monthStart.Month, …)`,
        // taking the label out of the boundary INSTANT. The January row is the one
        // that matters most, and `dotnet-architect` found it: the boundary crosses
        // the YEAR too, so a January-2026 bucket rendered as December 2025 — and
        // the DTO carries both fields.
        var window = new SwedishCalendar().MonthWindow(CivilMonth.Of(year, month));

        // Still true of the instant — the trap did not go away, it stopped being
        // the only thing on offer.
        window.Start.Year.ShouldBe(expectedStartYear);
        window.Start.Month.ShouldBe(expectedStartMonth);
        window.Start.Day.ShouldBe(expectedStartDay);

        // And this is the answer that was always meant to be read, sitting in the
        // same value.
        window.Month.Year.ShouldBe(year);
        window.Month.Month.ShouldBe(month);
    }

    [Theory]
    [InlineData(2026, 1, 2025, 12, 31, 23)]   // January starts 31 Dec 23:00Z (winter, UTC+1)
    [InlineData(2026, 7, 2026, 6, 30, 22)]    // July starts 30 Jun 22:00Z (summer, UTC+2)
    public void MonthWindow_StartUsesTheSwedishBoundary(
        int year, int month, int expectedYear, int expectedMonth, int expectedDay, int expectedHour)
    {
        var calendar = new SwedishCalendar();

        var start = calendar.MonthWindow(CivilMonth.Of(year, month)).Start;

        start.ToUniversalTime().ShouldBe(
            new DateTimeOffset(expectedYear, expectedMonth, expectedDay, expectedHour, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    // 00:30 Swedish on the 1st, both polarities. The UTC instant still says the
    // PREVIOUS month — in January, the previous YEAR as well.
    [InlineData(2026, 7, 31, 22, 30, 2026, 8)]    // 2026-08-01 00:30 CEST
    [InlineData(2025, 12, 31, 23, 30, 2026, 1)]   // 2026-01-01 00:30 CET
    // Mid-month, where UTC and Sweden agree — the control.
    [InlineData(2026, 6, 15, 9, 0, 2026, 6)]
    // Exactly ON the boundary instant: the month it opens, matching
    // StartOfDay_IsIdempotent_OnItsOwnResult's claim for the day.
    [InlineData(2026, 7, 31, 22, 0, 2026, 8)]
    public void MonthOf_ReadsTheSwedishWallClock_NotTheUtcInstantAndNotABoundary(
        int y, int mo, int d, int h, int mi, int expectedYear, int expectedMonth)
    {
        // Both tempting shortcuts fail here, and neither fails often enough to be
        // noticed: `instant.Month` is wrong for the first 1-2 hours of every
        // Swedish month, and `StartOfDay(instant).Month` is wrong on the FIRST of
        // every month all day, because that value is the previous day's 22:00Z or
        // 23:00Z.
        var month = new SwedishCalendar()
            .MonthOf(new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero));

        month.Year.ShouldBe(expectedYear);
        month.Month.ShouldBe(expectedMonth);
    }

    [Fact]
    public void MonthOf_IsNotStartOfDayDotMonth_OnTheFirstOfTheMonth()
    {
        // The shortcut spelled out, so a future reader does not re-invent it. On
        // 1 June 2026 at 10:00 Swedish, StartOfDay returns 2026-05-31T22:00Z —
        // whose .Month is MAY. Both readings are of the same instant; only one is
        // a civil month.
        var calendar = new SwedishCalendar();
        var tenAmOnTheFirstOfJune = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

        calendar.StartOfDay(tenAmOnTheFirstOfJune).Month.ShouldBe(5);
        calendar.MonthOf(tenAmOnTheFirstOfJune).Month.ShouldBe(6);
    }
}
