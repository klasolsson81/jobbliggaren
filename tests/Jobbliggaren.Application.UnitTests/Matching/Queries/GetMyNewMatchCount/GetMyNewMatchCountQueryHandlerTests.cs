using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.GetMyNewMatchCount;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — the Översikt "Nya matchningar"-count handler. Mirrors the EF-InMemory
/// <see cref="TestAppDbContextFactory"/> pattern (the handler queries <c>IAppDbContext</c>
/// directly), with NSubstitute <see cref="ICurrentUser"/>. The handler contract:
/// <list type="bullet">
/// <item>no authenticated user → honest <see cref="MyNewMatchCountDto.Zero"/> (no query);</item>
/// <item>null watermark (<c>LastSeenMatchesAt</c>) → EVERY match counts (never opened the view);</item>
/// <item>a set watermark → only <c>CreatedAt &gt; watermark</c> counts;</item>
/// <item>owner-scoped — another user's matches are never counted;</item>
/// <item>soft-deleted matches are excluded by the global query filter.</item>
/// </list>
/// NOTE on the watermark boundary: the join + watermark ROUND-TRIP against a relational
/// provider is pinned by the Testcontainers sibling (MyMatchesSurfaceTests) — InMemory can
/// drift on <c>DateTimeOffset</c> comparison semantics, so the in-memory tests assert the
/// owner-scope / null-vs-set / soft-delete BRANCHES, and the real-DB coherence loop is the
/// integration oracle.
/// </summary>
public class GetMyNewMatchCountQueryHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public GetMyNewMatchCountQueryHandlerTests()
    {
        // A fixed clock so UserJobAdMatch.Create / JobSeeker stamps are deterministic; the
        // handler itself reads no clock (count is watermark-relative).
        _clock.UtcNow.Returns(T0);
    }

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private void SeedMatch(AppDbContext db, Guid userId, DateTimeOffset createdAt)
    {
        _clock.UtcNow.Returns(createdAt);
        var match = UserJobAdMatch.Create(
            userId, JobAdId.New(), NotifiableMatchGrade.Good, ["csharp"], _clock).Value;
        db.UserJobAdMatches.Add(match);
        _clock.UtcNow.Returns(T0);
    }

    private void SeedSeeker(AppDbContext db, Guid userId, DateTimeOffset? lastSeen)
    {
        var seeker = JobSeeker.Register(userId, "Test User", _clock).Value;
        if (lastSeen is { } seen)
        {
            // SetLastSeenMatches advances to clock.UtcNow (monotonic) — stamp the watermark by
            // pointing the clock at the desired instant for the single call.
            _clock.UtcNow.Returns(seen);
            seeker.SetLastSeenMatches(_clock);
            _clock.UtcNow.Returns(T0);
            seeker.LastSeenMatchesAt!.Value.ShouldBe(seen);
        }

        db.JobSeekers.Add(seeker);
    }

    // =================================================================
    // No authenticated user → honest Zero, no query.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnZero_WhenNoAuthenticatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        // Seed a match for SOME user — the absence of a current user must still yield 0.
        SeedMatch(db, Guid.NewGuid(), T0.AddDays(1));
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(null));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.ShouldBe(MyNewMatchCountDto.Zero);
        result.Count.ShouldBe(0);
    }

    // =================================================================
    // Null watermark → ALL the user's matches are new.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldCountAllMatches_WhenWatermarkIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        // Seeker with a NULL LastSeenMatchesAt (never opened the matches view).
        SeedSeeker(db, userId, lastSeen: null);
        SeedMatch(db, userId, T0.AddDays(1));
        SeedMatch(db, userId, T0.AddDays(2));
        SeedMatch(db, userId, T0.AddDays(3));
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        // null watermark = every match counts as new.
        result.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_ShouldCountAllMatches_WhenNoJobSeekerExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        // No JobSeeker row at all → FirstOrDefault yields null watermark → all new (honest:
        // a user with matches but no seeker row still sees every match as new).
        SeedMatch(db, userId, T0.AddDays(1));
        SeedMatch(db, userId, T0.AddDays(2));
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.Count.ShouldBe(2);
    }

    // =================================================================
    // Set watermark → only matches AFTER the watermark count.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldCountOnlyMatchesAfterWatermark_WhenWatermarkIsSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        // Watermark at day 2: matches at day 1 (before) excluded; day 3, day 4 (after) counted.
        // The day-2 match is AT the watermark (strict >) → excluded.
        SeedSeeker(db, userId, lastSeen: T0.AddDays(2));
        SeedMatch(db, userId, T0.AddDays(1));   // before → excluded
        SeedMatch(db, userId, T0.AddDays(2));   // exactly at watermark → excluded (strict >)
        SeedMatch(db, userId, T0.AddDays(3));   // after → counted
        SeedMatch(db, userId, T0.AddDays(4));   // after → counted
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.Count.ShouldBe(2);
    }

    // =================================================================
    // Owner-scoped — another user's matches are never counted.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldCountOnlyOwnMatches_WhenAnotherUserHasMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        SeedSeeker(db, me, lastSeen: null);
        SeedMatch(db, me, T0.AddDays(1));
        // Another user's matches (any watermark) must not leak into MY count.
        SeedMatch(db, other, T0.AddDays(1));
        SeedMatch(db, other, T0.AddDays(2));
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(me));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.Count.ShouldBe(1);
    }

    // =================================================================
    // Soft-deleted matches excluded by the global query filter.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldExcludeSoftDeletedMatches_WhenWatermarkIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        SeedSeeker(db, userId, lastSeen: null);

        // One live, one soft-deleted (Art.17 erasure / expired-ad cascade) — only the live
        // one counts. InMemory honours the HasQueryFilter(DeletedAt == null) registered on
        // the model, so this branch is observable here (the real-DB filter is re-proven by
        // the persistence sibling).
        _clock.UtcNow.Returns(T0.AddDays(1));
        var live = UserJobAdMatch.Create(
            userId, JobAdId.New(), NotifiableMatchGrade.Good, ["csharp"], _clock).Value;
        var deleted = UserJobAdMatch.Create(
            userId, JobAdId.New(), NotifiableMatchGrade.Strong, ["sql"], _clock).Value;
        deleted.SoftDelete(_clock);
        _clock.UtcNow.Returns(T0);

        db.UserJobAdMatches.AddRange(live, deleted);
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.Count.ShouldBe(1);
    }

    // =================================================================
    // TD-114 — status-agnostic: a Failed (stranded-notification) match still counts.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldCountFailedMatch_StatusAgnostic()
    {
        // A match whose notification stranded and was reaped to Failed is STILL a real match —
        // the count must not filter on NotificationStatus (delivery status != match validity).
        // Regression lock against a future status filter that would hide stranded matches.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        SeedSeeker(db, userId, lastSeen: null); // never opened → every match is new

        _clock.UtcNow.Returns(T0.AddDays(1));
        var failed = UserJobAdMatch.Create(
            userId, JobAdId.New(), NotifiableMatchGrade.Top, ["csharp"], _clock).Value;
        failed.MarkQueued();
        failed.MarkFailed();
        _clock.UtcNow.Returns(T0);

        db.UserJobAdMatches.Add(failed);
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.Count.ShouldBe(1);
    }
}
