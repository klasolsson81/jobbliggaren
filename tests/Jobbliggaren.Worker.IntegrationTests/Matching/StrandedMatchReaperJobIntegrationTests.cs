using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// TD-114 — Testcontainers integration test for <see cref="StrandedMatchReaperJob"/> +
/// <see cref="StrandedMatchReaperWorker"/> against REAL Postgres. Proves two things EF-InMemory +
/// the unit tests cannot: (1) the Worker DI chain resolves the NEW reaper (the Worker runs
/// ValidateOnBuild=false / TD-103, so a missing dependency would only surface at Hangfire-invocation
/// — this test IS that invocation), and (2) the <c>NotificationStatus.Queued → Failed</c> transition
/// round-trips through the real <c>varchar(20)</c> by-name conversion + the
/// <c>WHERE status == Queued AND CreatedAt &lt; cutoff</c> query runs against real Postgres.
/// The match references UserId/JobAdId by identity (no FK, ADR 0058/0059), so a minimal Queued row
/// suffices — no ad/user seeding needed.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class StrandedMatchReaperJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // Far in the past → stranded under any plausible "now" (the reaper's cutoff is now − 48h).
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ResolvesFromDI_AndDoesNotThrow_WhenNothingStranded()
    {
        var ct = TestContext.Current.CancellationToken;

        // No stranded rows → clean no-op. The point is the DI resolution + a green run
        // (StrandedMatchReaperWorker → StrandedMatchReaperJob → IAppDbContext + IDateTimeProvider).
        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<StrandedMatchReaperWorker>();
            await worker.RunAsync(ct);
        });
    }

    [Fact]
    public async Task RunAsync_MarksLongStrandedQueuedMatch_Failed_AgainstRealPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();

        // Seed a Queued match created long ago (definitely past the 48h threshold).
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var match = UserJobAdMatch.Create(
                userId, jobAdId, NotifiableMatchGrade.Top, ["csharp"], new FixedClock(LongAgo)).Value;
            match.MarkQueued().IsSuccess.ShouldBeTrue();
            db.UserJobAdMatches.Add(match);
            await db.SaveChangesAsync(ct);
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<StrandedMatchReaperWorker>();
            await worker.RunAsync(ct);
        }

        // The Queued row transitioned to terminal Failed, round-tripped through real Postgres.
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloaded = await db.UserJobAdMatches.AsNoTracking()
                .SingleAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);
            reloaded.NotificationStatus.ShouldBe(NotificationStatus.Failed);
        }
    }

    // --------------------------- #453 (audit #26) - company-follow rail against real Postgres

    [Fact]
    public async Task RunAsync_MarksLongStrandedQueuedFollowHit_Failed_AgainstRealPostgres()
    {
        // Proves the follow arm's `Queued -> Failed` round-trips the real varchar(20) by-name conversion
        // and the `WHERE status == Queued AND CreatedAt < cutoff` query over followed_company_ad_hits.
        var ct = TestContext.Current.CancellationToken;
        var (userId, jobAdId, watchId) = await SeedQueuedFollowHitAsync(LongAgo, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<StrandedMatchReaperWorker>();
            await worker.RunAsync(ct);
        }

        var reloaded = await GetFollowHitAsync(userId, jobAdId, watchId, ct);
        reloaded.ShouldNotBeNull();
        reloaded.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_LeavesFreshQueuedFollowHit_Queued_AgainstRealPostgres()
    {
        // A follow-hit created ~now is far within the 48h threshold -> not stranded -> stays Queued.
        var ct = TestContext.Current.CancellationToken;
        var (userId, jobAdId, watchId) = await SeedQueuedFollowHitAsync(DateTimeOffset.UtcNow, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<StrandedMatchReaperWorker>();
            await worker.RunAsync(ct);
        }

        var reloaded = await GetFollowHitAsync(userId, jobAdId, watchId, ct);
        reloaded.ShouldNotBeNull();
        reloaded.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued);
    }

    // RunAsync_ExcludesSoftDeletedQueuedFollowHit_AgainstRealPostgres retired by #868: the soft-delete
    // axis it fabricated no longer exists (writerless decoy, removed with the column). The reaper reads
    // all Queued+aged hits unfiltered; the live reap/leave behaviour is covered by the tests above/below.

    [Fact]
    public async Task RunAsync_ReapsBothRails_InSingleRun_AgainstRealPostgres()
    {
        // Both rails reaped in one run: a stranded match AND a stranded follow-hit both -> Failed.
        var ct = TestContext.Current.CancellationToken;
        var matchUserId = Guid.NewGuid();
        var matchAdId = JobAdId.New();
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var match = UserJobAdMatch.Create(
                matchUserId, matchAdId, NotifiableMatchGrade.Top, ["csharp"], new FixedClock(LongAgo)).Value;
            match.MarkQueued().IsSuccess.ShouldBeTrue();
            db.UserJobAdMatches.Add(match);
            await db.SaveChangesAsync(ct);
        }
        var (followUserId, followAdId, followWatchId) = await SeedQueuedFollowHitAsync(LongAgo, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<StrandedMatchReaperWorker>();
            await worker.RunAsync(ct);
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.UserJobAdMatches.AsNoTracking()
                .SingleAsync(m => m.UserId == matchUserId && m.JobAdId == matchAdId, ct))
                .NotificationStatus.ShouldBe(NotificationStatus.Failed);
        }
        var reloadedHit = await GetFollowHitAsync(followUserId, followAdId, followWatchId, ct);
        reloadedHit.ShouldNotBeNull();
        reloadedHit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Failed);
    }

    // Seeds a Queued FollowedCompanyAdHit created at <paramref name="createdAt"/> for a fresh user.
    // No FK on job_ad_id / company_watch_id (ADR 0058/0059), so fresh ids suffice - no ad/watch seeding.
    private async Task<(Guid UserId, JobAdId JobAdId, CompanyWatchId WatchId)> SeedQueuedFollowHitAsync(
        DateTimeOffset createdAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(createdAt);
        var userId = Guid.NewGuid();
        var hit = FollowedCompanyAdHit.Create(userId, JobAdId.New(), CompanyWatchId.New(), clock).Value;
        hit.MarkQueued().IsSuccess.ShouldBeTrue();
        db.FollowedCompanyAdHits.Add(hit);
        await db.SaveChangesAsync(ct);
        return (userId, hit.JobAdId, hit.CompanyWatchId);
    }

    private async Task<FollowedCompanyAdHit?> GetFollowHitAsync(
        Guid userId, JobAdId jobAdId, CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits.AsNoTracking().FirstOrDefaultAsync(
            h => h.UserId == userId && h.JobAdId == jobAdId && h.CompanyWatchId == watchId, ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
