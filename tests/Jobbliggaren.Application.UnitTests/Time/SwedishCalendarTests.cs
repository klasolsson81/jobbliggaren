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
