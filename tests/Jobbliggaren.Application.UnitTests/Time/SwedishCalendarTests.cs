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

    [Fact]
    public void StartOfMonth_ReturnsAZeroOffsetInstant()
    {
        // Same contract on the sibling member, so the follow-up that consumes
        // it cannot inherit the defect.
        new SwedishCalendar().StartOfMonth(2026, 7).Offset.ShouldBe(TimeSpan.Zero);
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

    [Fact]
    public void StartOfMonth_IsNotReproducibleByAddMonths_AcrossADstBoundary()
    {
        // The named follow-up consumer builds a 12-month series, and the cheap
        // way is one anchor plus AddMonths. It is wrong TWICE — and an earlier
        // version of this comment named only the second reason, because it was
        // written against the pre-normalisation implementation:
        //
        //   1. The normalised return is NOT the 1st of the month.
        //      StartOfMonth(2026, 7) is 2026-06-30T22:00Z, so stepping back six
        //      months lands on 30 December, not 31.
        //   2. AddMonths preserves the offset, so it is an hour out as well.
        //
        // Pinned by VALUE, not by inequality: ShouldNotBe held for any
        // difference at all, which is exactly why it never noticed that its
        // stated reason had stopped being true.
        var calendar = new SwedishCalendar();

        calendar.StartOfMonth(2026, 7).ShouldBe(
            new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero));
        calendar.StartOfMonth(2026, 7).AddMonths(-6).ShouldBe(
            new DateTimeOffset(2025, 12, 30, 22, 0, 0, TimeSpan.Zero));
        calendar.StartOfMonth(2026, 1).ShouldBe(
            new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfMonth_ReturnValueIsNotTheFirstOfTheMonth()
    {
        // A trap the UTC normalisation CREATED, and the named follow-up consumer
        // already reads exactly this: ApplicationStatsCalculator labels each
        // bucket with monthStart.Month. StartOfMonth(2026, 7) is 2026-06-30T22:00Z,
        // so a July bucket would be labelled JUNE — a visibly mislabelled graph.
        //
        // Labels come from the ARGUMENTS, never from the return value.
        var start = new SwedishCalendar().StartOfMonth(2026, 7);

        start.Month.ShouldBe(6);
        start.Day.ShouldBe(30);
    }

    [Theory]
    [InlineData(2026, 1, 2025, 12, 31, 23)]   // January starts 31 Dec 23:00Z (winter, UTC+1)
    [InlineData(2026, 7, 2026, 6, 30, 22)]    // July starts 30 Jun 22:00Z (summer, UTC+2)
    public void StartOfMonth_UsesTheSwedishBoundary(
        int year, int month, int expectedYear, int expectedMonth, int expectedDay, int expectedHour)
    {
        var calendar = new SwedishCalendar();

        var start = calendar.StartOfMonth(year, month);

        start.ToUniversalTime().ShouldBe(
            new DateTimeOffset(expectedYear, expectedMonth, expectedDay, expectedHour, 0, 0, TimeSpan.Zero));
    }
}
