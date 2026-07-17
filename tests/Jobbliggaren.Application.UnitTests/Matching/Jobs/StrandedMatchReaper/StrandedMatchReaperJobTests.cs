using Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.StrandedMatchReaper;

/// <summary>
/// TD-114 — unit cover for the stranded-Queued reaper (ADR 0080 Vag 4). Pins the load-bearing
/// invariants: a row Queued past the 48h threshold is moved to terminal <c>Failed</c>; a fresh
/// Queued row (just claimed, mid-flight) is left alone (threshold boundary); Pending/Sent rows
/// are never touched; an empty sweep is a no-op. The IAppDbContext is the established
/// real-AppDbContext-over-EF-InMemory fake (TestAppDbContextFactory) so the
/// <c>Where(status == Queued &amp;&amp; CreatedAt &lt; cutoff)</c> query runs as a genuine IQueryable.
/// </summary>
public class StrandedMatchReaperJobTests
{
    // The reaper "now". Threshold = 48h, so the cutoff is 2026-06-23 04:45Z.
    private static readonly DateTimeOffset Now =
        new(2026, 6, 25, 4, 45, 0, TimeSpan.Zero);

    private static readonly FakeDateTimeProvider NowClock = new(Now);

    private static StrandedMatchReaperJob CreateJob(AppDbContext db) =>
        new(db, NowClock, NullLogger<StrandedMatchReaperJob>.Instance);

    /// <summary>Seeds a match created at <paramref name="createdAt"/> in the target status.</summary>
    private static UserJobAdMatch Seed(AppDbContext db, DateTimeOffset createdAt, NotificationStatus status)
    {
        var clock = new FakeDateTimeProvider(createdAt);
        var match = UserJobAdMatch.Create(
            Guid.NewGuid(), JobAdId.New(), NotifiableMatchGrade.Strong, ["csharp"], clock).Value;

        if (status is NotificationStatus.Queued or NotificationStatus.Sent or NotificationStatus.Failed)
            match.MarkQueued();
        if (status == NotificationStatus.Sent)
            match.MarkSent(clock);
        if (status == NotificationStatus.Failed)
            match.MarkFailed();

        db.UserJobAdMatches.Add(match);
        return match;
    }

    [Fact]
    public async Task RunAsync_QueuedRowOlderThanThreshold_IsMarkedFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var stranded = Seed(db, Now.AddHours(-50), NotificationStatus.Queued); // 50h > 48h
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        var reloaded = await db.UserJobAdMatches.SingleAsync(m => m.Id == stranded.Id, ct);
        reloaded.NotificationStatus.ShouldBe(NotificationStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_FreshlyQueuedRowWithinThreshold_IsLeftUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var fresh = Seed(db, Now.AddHours(-1), NotificationStatus.Queued); // mid-flight, 1h
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        var reloaded = await db.UserJobAdMatches.SingleAsync(m => m.Id == fresh.Id, ct);
        reloaded.NotificationStatus.ShouldBe(NotificationStatus.Queued);
    }

    [Fact]
    public async Task RunAsync_PendingAndSentRows_AreNeverTouched_EvenWhenOld()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var pending = Seed(db, Now.AddHours(-50), NotificationStatus.Pending);
        var sent = Seed(db, Now.AddHours(-50), NotificationStatus.Sent);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        (await db.UserJobAdMatches.SingleAsync(m => m.Id == pending.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Pending);
        (await db.UserJobAdMatches.SingleAsync(m => m.Id == sent.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task RunAsync_OnlyReapsStrandedQueued_LeavingTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var stranded = Seed(db, Now.AddHours(-50), NotificationStatus.Queued);
        var fresh = Seed(db, Now.AddHours(-2), NotificationStatus.Queued);
        var pending = Seed(db, Now.AddHours(-50), NotificationStatus.Pending);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        (await db.UserJobAdMatches.SingleAsync(m => m.Id == stranded.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Failed);
        (await db.UserJobAdMatches.SingleAsync(m => m.Id == fresh.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Queued);
        (await db.UserJobAdMatches.SingleAsync(m => m.Id == pending.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_WhenNothingStranded_IsANoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        Seed(db, Now.AddHours(-2), NotificationStatus.Queued); // fresh only
        await db.SaveChangesAsync(ct);

        // Idempotent / safe on an empty sweep — must not throw.
        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));
    }

    [Fact]
    public async Task RunAsync_QueuedRowExactlyAtThreshold_IsLeftUntouched()
    {
        // The predicate is strict `<` (CreatedAt < now-48h), so a row created EXACTLY at the
        // cutoff is NOT stranded. Pins the off-by-one (flipping `<`→`<=` would reap this).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var atBoundary = Seed(db, Now.AddHours(-48), NotificationStatus.Queued);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        (await db.UserJobAdMatches.SingleAsync(m => m.Id == atBoundary.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Queued);
    }

    [Fact]
    public async Task RunAsync_QueuedRowJustOverThreshold_IsMarkedFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var justOver = Seed(db, Now.AddHours(-48).AddSeconds(-1), NotificationStatus.Queued);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        (await db.UserJobAdMatches.SingleAsync(m => m.Id == justOver.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Failed);
    }

    // RunAsync_SoftDeletedStrandedQueuedRow_IsNotReaped retired by #868: the soft-delete axis it
    // fabricated no longer exists (writerless decoy, removed with the column). The reaper now reads all
    // Queued+aged rows unfiltered; the live reap/skip behaviour is covered by the tests above and below.

    [Fact]
    public async Task RunAsync_AlreadyFailedRow_IsLeftUntouchedBySecondRun()
    {
        // Failed is terminal at the query layer too: the reaper filters `status == Queued`, so a
        // row already reaped by a prior run is excluded (idempotent across runs — no re-processing,
        // no throw). Guards against a future predicate broadening (e.g. `status != Sent`).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var alreadyFailed = Seed(db, Now.AddHours(-50), NotificationStatus.Failed);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        (await db.UserJobAdMatches.SingleAsync(m => m.Id == alreadyFailed.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Failed);
    }

    // =================================================================
    // #453 (audit #26) - company-follow rail (FollowedCompanyAdHit). Same strand mechanism, a
    // different aggregate (no grade). The reaper's second arm queries
    // `Where(status == Queued && CreatedAt < cutoff)` over FollowedCompanyAdHits.
    // =================================================================

    /// <summary>Seeds a follow-hit created at <paramref name="createdAt"/> in the target status.</summary>
    private static FollowedCompanyAdHit SeedFollowHit(
        AppDbContext db, DateTimeOffset createdAt, FollowedCompanyAdHitStatus status)
    {
        var clock = new FakeDateTimeProvider(createdAt);
        var hit = FollowedCompanyAdHit.Create(
            Guid.NewGuid(), JobAdId.New(), CompanyWatchId.New(), clock).Value;

        if (status is FollowedCompanyAdHitStatus.Queued
            or FollowedCompanyAdHitStatus.Sent
            or FollowedCompanyAdHitStatus.Failed)
            hit.MarkQueued();
        if (status == FollowedCompanyAdHitStatus.Sent)
            hit.MarkSent(clock);
        if (status == FollowedCompanyAdHitStatus.Failed)
            hit.MarkFailed();

        db.FollowedCompanyAdHits.Add(hit);
        return hit;
    }

    [Fact]
    public async Task RunAsync_QueuedFollowHitOlderThanThreshold_IsMarkedFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var stranded = SeedFollowHit(db, Now.AddHours(-50), FollowedCompanyAdHitStatus.Queued); // 50h > 48h
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        var reloaded = await db.FollowedCompanyAdHits.SingleAsync(h => h.Id == stranded.Id, ct);
        reloaded.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_FreshlyQueuedFollowHitWithinThreshold_IsLeftUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var fresh = SeedFollowHit(db, Now.AddHours(-1), FollowedCompanyAdHitStatus.Queued); // mid-flight
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        var reloaded = await db.FollowedCompanyAdHits.SingleAsync(h => h.Id == fresh.Id, ct);
        reloaded.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued);
    }

    [Fact]
    public async Task RunAsync_PendingAndSentFollowHits_AreNeverTouched_EvenWhenOld()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var pending = SeedFollowHit(db, Now.AddHours(-50), FollowedCompanyAdHitStatus.Pending);
        var sent = SeedFollowHit(db, Now.AddHours(-50), FollowedCompanyAdHitStatus.Sent);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(ct);

        (await db.FollowedCompanyAdHits.SingleAsync(h => h.Id == pending.Id, ct))
            .NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
        (await db.FollowedCompanyAdHits.SingleAsync(h => h.Id == sent.Id, ct))
            .NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
    }

    // RunAsync_SoftDeletedStrandedQueuedFollowHit_IsNotReaped retired by #868 (parity the match arm):
    // the follow-hit soft-delete axis it fabricated no longer exists.

    [Fact]
    public async Task RunAsync_ReapsBothRails_StrandedMatchAndFollowHit_InOneAtomicSave()
    {
        // #453 audit #26 - a stranded match AND a stranded follow-hit are both reaped in ONE run and
        // committed by a SINGLE atomic SaveChanges (asserted via the SavingChanges event count).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        var strandedMatch = Seed(db, Now.AddHours(-50), NotificationStatus.Queued);
        var strandedHit = SeedFollowHit(db, Now.AddHours(-50), FollowedCompanyAdHitStatus.Queued);
        await db.SaveChangesAsync(ct);

        var saveCount = 0;
        db.SavingChanges += (_, _) => saveCount++;

        await CreateJob(db).RunAsync(ct);

        saveCount.ShouldBe(1, "both rails commit in a single atomic SaveChanges");
        (await db.UserJobAdMatches.SingleAsync(m => m.Id == strandedMatch.Id, ct))
            .NotificationStatus.ShouldBe(NotificationStatus.Failed);
        (await db.FollowedCompanyAdHits.SingleAsync(h => h.Id == strandedHit.Id, ct))
            .NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_WhenNothingStrandedOnEitherRail_DoesNotSaveChanges()
    {
        // Early-return: when BOTH arms reap 0, RunAsync returns before SaveChanges (no wasted write).
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestAppDbContextFactory.Create();
        Seed(db, Now.AddHours(-2), NotificationStatus.Queued);          // fresh match (within threshold)
        SeedFollowHit(db, Now.AddHours(-2), FollowedCompanyAdHitStatus.Queued); // fresh follow-hit
        await db.SaveChangesAsync(ct);

        var saveCount = 0;
        db.SavingChanges += (_, _) => saveCount++;

        await CreateJob(db).RunAsync(ct);

        saveCount.ShouldBe(0, "nothing stranded on either rail -> early return, no SaveChanges");
    }
}
