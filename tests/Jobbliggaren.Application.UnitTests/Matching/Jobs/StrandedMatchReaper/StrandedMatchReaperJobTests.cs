using Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper;
using Jobbliggaren.Application.UnitTests.Common;
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
}
