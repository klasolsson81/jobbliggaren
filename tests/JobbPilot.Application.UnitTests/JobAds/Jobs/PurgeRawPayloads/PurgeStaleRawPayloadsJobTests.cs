using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Jobs.PurgeRawPayloads;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Jobs.PurgeRawPayloads;

/// <summary>
/// F2-P8c — guard-paths för PurgeStaleRawPayloadsJob. Den faktiska
/// <c>ExecuteUpdateAsync</c>-translation kräver riktig DB-provider och täcks
/// av Worker.IntegrationTests / Api.IntegrationTests när P8c-deploy körs
/// (per CTO-rond 2026-05-13 punkt 5+8). Här verifieras endast invarianten
/// "ogiltig retention → ingen DB-mutation" + att DbSet inte rörs vid skip.
/// </summary>
public class PurgeStaleRawPayloadsJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static PurgeStaleRawPayloadsJob CreateJob(
        IAppDbContext db,
        int retentionDays,
        ISystemEventAuditor? auditor = null) =>
        new(
            db,
            new FakeDateTimeProvider(Now),
            Options.Create(new JobSourceRetentionOptions { RawPayloadRetentionDays = retentionDays }),
            auditor ?? Substitute.For<ISystemEventAuditor>(),
            NullLogger<PurgeStaleRawPayloadsJob>.Instance);

    [Fact]
    public async Task RunAsync_WhenRetentionDaysIsZero_SkipsWithoutTouchingDbSet()
    {
        var db = Substitute.For<IAppDbContext>();
        var jobAdSet = Substitute.For<DbSet<JobAd>>();
        db.JobAds.Returns(jobAdSet);
        var job = CreateJob(db, retentionDays: 0);

        await job.RunAsync(TestContext.Current.CancellationToken);

        // Range-guard: ingen DbSet-access ska ske vid invalid config.
        _ = db.DidNotReceive().JobAds;
    }

    [Fact]
    public async Task RunAsync_WhenRetentionDaysIsNegative_SkipsWithoutTouchingDbSet()
    {
        var db = Substitute.For<IAppDbContext>();
        var jobAdSet = Substitute.For<DbSet<JobAd>>();
        db.JobAds.Returns(jobAdSet);
        var job = CreateJob(db, retentionDays: -5);

        await job.RunAsync(TestContext.Current.CancellationToken);

        _ = db.DidNotReceive().JobAds;
    }

    [Fact]
    public async Task RunAsync_WithValidRetention_AccessesJobAdsDbSet()
    {
        // Med riktig in-memory DB (utan rader) ska jobbet köra utan kast.
        // ExecuteUpdateAsync-translation testas i Worker.IntegrationTests
        // (kräver Postgres-provider).
        var db = TestAppDbContextFactory.Create();
        var job = new PurgeStaleRawPayloadsJob(
            db,
            new FakeDateTimeProvider(Now),
            Options.Create(new JobSourceRetentionOptions { RawPayloadRetentionDays = 30 }),
            Substitute.For<ISystemEventAuditor>(),
            NullLogger<PurgeStaleRawPayloadsJob>.Instance);

        // InMemory-provider stöder inte ExecuteUpdateAsync — vi förväntar oss
        // antingen NotSupportedException eller InvalidOperationException.
        // Det är OK: vi vill bara verifiera att retentionDays-guarden inte
        // tände → koden kommer till ExecuteUpdateAsync-anropet.
        var act = async () => await job.RunAsync(TestContext.Current.CancellationToken);
        var ex = await Should.ThrowAsync<Exception>(act);
        ex.ShouldSatisfyAllConditions(
            () => ex.ShouldNotBeNull(),
            () => (ex is InvalidOperationException || ex is NotSupportedException)
                .ShouldBeTrue($"Expected InMemory-provider ExecuteUpdate-fail, fick {ex.GetType().Name}: {ex.Message}"));
    }
}
