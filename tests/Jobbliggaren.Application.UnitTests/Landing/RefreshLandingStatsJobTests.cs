using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Landing;

public class RefreshLandingStatsJobTests
{
    // The real calendar, not a stub. It is pure and deterministic, and stubbing
    // the boundary would let these tests assert "nya idag" off a value no
    // production adapter emits — the premise rule in CLAUDE.md §5 `Tests:`. It
    // also means the Swedish boundary is exercised here rather than asserted
    // twice in two places.
    private static readonly SwedishCalendar Calendar = new();

    private static JobAd CreateJobAd(FakeDateTimeProvider clock, string title, DateTimeOffset publishedAt) =>
        JobAd.Create(
            title,
            Company.Create("Acme").Value,
            "Description",
            $"https://example.com/{title}",
            JobSource.Manual,
            publishedAt,
            publishedAt.AddDays(30),
            clock).Value;

    [Fact]
    public async Task RunAsync_CountsActiveJobAds_WritesToCache()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(new DateTimeOffset(2026, 5, 23, 14, 0, 0, TimeSpan.Zero));
        // 23 May is Swedish summer time (UTC+2), so the Swedish day began at
        // 22:00Z on the 22nd — NOT at UTC midnight on the 23rd.
        var swedishDayStart = new DateTimeOffset(2026, 5, 22, 22, 0, 0, TimeSpan.Zero);
        var db = TestAppDbContextFactory.Create();

        // 3 published during the Swedish day, 2 before it; 5 active in total.
        db.JobAds.Add(CreateJobAd(clock, "today-1", swedishDayStart.AddHours(3)));
        db.JobAds.Add(CreateJobAd(clock, "today-2", swedishDayStart.AddHours(10)));
        db.JobAds.Add(CreateJobAd(clock, "today-3", swedishDayStart.AddHours(15)));
        db.JobAds.Add(CreateJobAd(clock, "yesterday-1", swedishDayStart.AddDays(-1)));
        db.JobAds.Add(CreateJobAd(clock, "yesterday-2", swedishDayStart.AddHours(-5)));
        await db.SaveChangesAsync(ct);

        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(db, clock, Calendar, cache, NullLogger<RefreshLandingStatsJob>.Instance);
        await job.RunAsync(ct);

        captured.ShouldNotBeNull();
        captured!.ActiveCount.ShouldBe(5);
        captured.NewToday.ShouldBe(3);
        captured.IsStale.ShouldBeFalse();
        captured.RefreshedAt.ShouldBe(clock.UtcNow);
    }

    // Klas-direktiv 2026-07-28, and the case that actually discriminates the two
    // boundaries: an ad published at 22:30Z on 22 May is 00:30 on 23 May in
    // Sweden. Under the retired UTC boundary it counted as YESTERDAY for another
    // 1.5 hours; under the Swedish one it is today. The sibling at 21:30Z is
    // 23:30 on the 22nd in Sweden and must stay out — without it, a boundary
    // moved a day too far would also pass.
    [Fact]
    public async Task RunAsync_AdPublishedJustAfterSwedishMidnight_CountsAsToday()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(new DateTimeOffset(2026, 5, 23, 1, 0, 0, TimeSpan.Zero));
        var db = TestAppDbContextFactory.Create();

        // 00:30 Swedish time on 23 May — inside the Swedish day, outside the UTC one.
        db.JobAds.Add(CreateJobAd(clock, "just-after-swedish-midnight",
            new DateTimeOffset(2026, 5, 22, 22, 30, 0, TimeSpan.Zero)));
        // 23:30 Swedish time on 22 May — outside both.
        db.JobAds.Add(CreateJobAd(clock, "just-before-swedish-midnight",
            new DateTimeOffset(2026, 5, 22, 21, 30, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync(ct);

        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(db, clock, Calendar, cache, NullLogger<RefreshLandingStatsJob>.Instance);
        await job.RunAsync(ct);

        captured.ShouldNotBeNull();
        captured!.ActiveCount.ShouldBe(2);
        captured.NewToday.ShouldBe(1,
            "en annons publicerad 00:30 svensk tid hör till den svenska dagen; " +
            "den som publicerades 23:30 kvällen innan gör det inte. Den retirerade " +
            "UTC-gränsen hade gett 0 här.");
    }

    [Fact]
    public async Task RunAsync_EmptyDatabase_WritesZeroCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = FakeDateTimeProvider.Default;
        var db = TestAppDbContextFactory.Create();
        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(db, clock, Calendar, cache, NullLogger<RefreshLandingStatsJob>.Instance);
        await job.RunAsync(ct);

        captured.ShouldNotBeNull();
        captured!.ActiveCount.ShouldBe(0);
        captured.NewToday.ShouldBe(0);
        captured.IsStale.ShouldBeFalse(); // Worker har faktiskt kört — IsStale=false även om resultatet är 0.
    }

    // #864 follow-up (B4) — the landing page's public numbers count ONLY Active ads, pinned on
    // the row where the allow-list (== Active) and a deny-list (!= Archived) disagree: the Erased
    // tombstone (#842, real Art. 17 transition — reachable since #886). Both job gates read pure
    // Status (the newToday one adds PublishedAt, which Erase() does not touch), so an erased row
    // is fully reachable and ONLY the gate excludes it. A deny-list here would inflate the public
    // "aktiva annonser just nu" with GDPR tombstones. The archived row binds gate DELETION; the
    // erased row binds the flip. ASYMMETRIC seed: correct → 2/1 · deleted → 4/3 · deny-list →
    // 3/2 · inverted → 1/1. Transitions via the real domain methods (#843 / #864 AC 4).
    [Fact]
    public async Task RunAsync_CountsNeitherArchivedNorErasedAds_InEitherNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeDateTimeProvider(new DateTimeOffset(2026, 5, 23, 14, 0, 0, TimeSpan.Zero));
        // Anchor inside the Swedish day that began 22:00Z on the 22nd (summer, UTC+2).
        var duringSwedishDay = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var db = TestAppDbContextFactory.Create();

        // 2 active (1 today + 1 yesterday) + 1 archived today + 1 erased today.
        db.JobAds.Add(CreateJobAd(clock, "active-today", duringSwedishDay.AddHours(1)));
        db.JobAds.Add(CreateJobAd(clock, "active-yesterday", duringSwedishDay.AddDays(-1)));

        var archived = CreateJobAd(clock, "archived-today", duringSwedishDay.AddHours(2));
        archived.Archive(clock).IsSuccess.ShouldBeTrue("Archive-seeden får inte tyst misslyckas");
        db.JobAds.Add(archived);

        var erased = CreateJobAd(clock, "erased-today", duringSwedishDay.AddHours(3));
        erased.Erase(clock).IsSuccess.ShouldBeTrue("Erase-seeden får inte tyst misslyckas");
        db.JobAds.Add(erased);

        await db.SaveChangesAsync(ct);

        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(db, clock, Calendar, cache, NullLogger<RefreshLandingStatsJob>.Instance);
        await job.RunAsync(ct);

        captured.ShouldNotBeNull();
        captured!.ActiveCount.ShouldBe(2,
            "landningssidans totalsiffra räknar ENDAST Active — 2, inte 3 (deny-list: tombstonen " +
            "räknad), inte 4 (grinden raderad) och inte 1 (inverterad).");
        captured.NewToday.ShouldBe(1,
            "'nya idag' räknar ENDAST Active publicerade idag — 1, inte 2 (deny-list: tombstonen " +
            "publicerad idag räknad) och inte 3 (grinden raderad).");
    }
}
