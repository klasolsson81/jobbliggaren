using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Landing;

public class RefreshLandingStatsJobTests
{
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
        var todayUtcStart = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var db = TestAppDbContextFactory.Create();

        // 3 publicerade idag (UTC), 2 publicerade igår, totalt 5 aktiva.
        db.JobAds.Add(CreateJobAd(clock, "today-1", todayUtcStart.AddHours(1)));
        db.JobAds.Add(CreateJobAd(clock, "today-2", todayUtcStart.AddHours(8)));
        db.JobAds.Add(CreateJobAd(clock, "today-3", todayUtcStart.AddHours(13)));
        db.JobAds.Add(CreateJobAd(clock, "yesterday-1", todayUtcStart.AddDays(-1)));
        db.JobAds.Add(CreateJobAd(clock, "yesterday-2", todayUtcStart.AddDays(-1).AddHours(5)));
        await db.SaveChangesAsync(ct);

        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(db, clock, cache, NullLogger<RefreshLandingStatsJob>.Instance);
        await job.RunAsync(ct);

        captured.ShouldNotBeNull();
        captured!.ActiveCount.ShouldBe(5);
        captured.NewToday.ShouldBe(3);
        captured.IsStale.ShouldBeFalse();
        captured.RefreshedAt.ShouldBe(clock.UtcNow);
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

        var job = new RefreshLandingStatsJob(db, clock, cache, NullLogger<RefreshLandingStatsJob>.Instance);
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
        var todayUtcStart = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var db = TestAppDbContextFactory.Create();

        // 2 active (1 today + 1 yesterday) + 1 archived today + 1 erased today.
        db.JobAds.Add(CreateJobAd(clock, "active-today", todayUtcStart.AddHours(1)));
        db.JobAds.Add(CreateJobAd(clock, "active-yesterday", todayUtcStart.AddDays(-1)));

        var archived = CreateJobAd(clock, "archived-today", todayUtcStart.AddHours(2));
        archived.Archive(clock).IsSuccess.ShouldBeTrue("Archive-seeden får inte tyst misslyckas");
        db.JobAds.Add(archived);

        var erased = CreateJobAd(clock, "erased-today", todayUtcStart.AddHours(3));
        erased.Erase(clock).IsSuccess.ShouldBeTrue("Erase-seeden får inte tyst misslyckas");
        db.JobAds.Add(erased);

        await db.SaveChangesAsync(ct);

        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(db, clock, cache, NullLogger<RefreshLandingStatsJob>.Instance);
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
