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
}
