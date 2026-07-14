using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
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
/// <item>soft-deleted matches are excluded by the global query filter;</item>
/// <item>#864 — a match whose ad is NOT <c>Active</c> is not counted. The badge must count the
/// same presentable set the list shows, or Översikten renders "3 nya matchningar" above a view
/// with zero rows.</item>
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

    // Seeds an ACTIVE JobAd row and returns its id.
    //
    // #864 — this helper did not exist, and that was the defect in the suite. Every seed below used a
    // bare JobAdId.New() and NEVER inserted the ad, so the whole suite proved a count over matches
    // whose ads DO NOT EXIST. That was invisible while the handler joined nothing; but its paired
    // surface — GetMyMatches — has always INNER JOINED JobAds, so the badge and the list already
    // disagreed on exactly this seed. A suite that cannot represent the production state cannot
    // observe an incoherence with the surface it is paired against. The count is now lifecycle-gated
    // (#864), so the ad must be real; each test's own axis (watermark / owner-scope / soft-delete /
    // notification-status / uncapped) is untouched.
    private JobAdId SeedActiveAd(AppDbContext db)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: "Roll",
            company: Company.Create("Bolag AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: T0,
            expiresAt: T0.AddDays(60),
            clock: _clock).Value;
        db.JobAds.Add(jobAd);
        return jobAd.Id;
    }

    private void SeedMatch(AppDbContext db, Guid userId, DateTimeOffset createdAt)
    {
        var adId = SeedActiveAd(db);
        _clock.UtcNow.Returns(createdAt);
        var match = UserJobAdMatch.Create(
            userId, adId, NotifiableMatchGrade.Good, ["csharp"], _clock).Value;
        db.UserJobAdMatches.Add(match);
        _clock.UtcNow.Returns(T0);
    }

    private void SeedSeeker(AppDbContext db, Guid userId, DateTimeOffset? lastSeen)
    {
        var seeker = JobSeeker.Register(userId, "Test User", _clock).Value;
        if (lastSeen is { } seen)
        {
            // Stamp the watermark directly at the desired instant (clock pointed at `seen` so
            // the aggregate's future-clamp does not bind).
            _clock.UtcNow.Returns(seen);
            seeker.SetLastSeenMatches(seen, _clock);
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
    public async Task Handle_ReturnsUncappedCount_WhenNewMatchesExceedTheListCap()
    {
        // #273 contract — the count is the TRUE total and is intentionally UNCAPPED: it may exceed
        // the 50-row cap of GetMyMatches. This unit project runs in the pre-commit hook (the
        // Testcontainers divergence oracle in MyMatchesSurfaceTests only runs in CI), so this is the
        // pre-push lock: a future Math.Min(count, 50) / .Take(51) clamp turns it red before push.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        // 53 matches (> the GetMyMatches 50-row cap), null watermark → all new.
        SeedSeeker(db, userId, lastSeen: null);
        const int newMatches = 53;
        for (var i = 0; i < newMatches; i++)
            SeedMatch(db, userId, T0.AddDays(i + 1));
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        // Uncapped: the true total, NOT clamped to the list's 50.
        result.Count.ShouldBe(newMatches);
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
        var liveAdId = SeedActiveAd(db);
        var deletedAdId = SeedActiveAd(db);
        _clock.UtcNow.Returns(T0.AddDays(1));
        var live = UserJobAdMatch.Create(
            userId, liveAdId, NotifiableMatchGrade.Good, ["csharp"], _clock).Value;
        var deleted = UserJobAdMatch.Create(
            userId, deletedAdId, NotifiableMatchGrade.Strong, ["sql"], _clock).Value;
        deleted.SoftDelete(_clock);
        _clock.UtcNow.Returns(T0);

        db.UserJobAdMatches.AddRange(live, deleted);
        await db.SaveChangesAsync(ct);

        var sut = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetMyNewMatchCountQuery(), ct);

        result.Count.ShouldBe(1);
    }

    // =================================================================
    // #864 — the badge does not count a match whose ad has been archived.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldNotCountMatch_WhenItsJobAdIsArchived()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        SeedSeeker(db, userId, lastSeen: null); // never opened → every match is new

        // Two matches: one to an Active ad, one to an ad archived AFTER the match was detected —
        // the ONLY way this state arises in production, since BackgroundMatchingJob gates
        // Status == Active before it scores. Archiving via the domain transition ExpireJobAdsJob
        // performs; never a fabricated column value (#843 / #864 AC 4).
        var liveAdId = SeedActiveAd(db);
        var archivedAdId = SeedActiveAd(db);
        _clock.UtcNow.Returns(T0.AddDays(1));
        db.UserJobAdMatches.AddRange(
            UserJobAdMatch.Create(userId, liveAdId, NotifiableMatchGrade.Good, ["csharp"], _clock).Value,
            UserJobAdMatch.Create(userId, archivedAdId, NotifiableMatchGrade.Strong, ["sql"], _clock).Value);
        _clock.UtcNow.Returns(T0);
        await db.SaveChangesAsync(ct);

        var archivedAd = db.JobAds.Single(j => j.Id == archivedAdId);
        archivedAd.Archive(_clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var result = await new GetMyNewMatchCountQueryHandler(db, UserWith(userId))
            .Handle(new GetMyNewMatchCountQuery(), ct);

        // 1, not 2. NON-VACUOUS BY CONSTRUCTION: the count must still find the ACTIVE match, so a
        // gate that excluded everything would fail this assertion too — "the archived one is not
        // counted" cannot pass by counting nothing.
        result.Count.ShouldBe(1,
            "badgen får inte räkna en matchning vars annons är arkiverad — den räknar samma " +
            "presenterbara mängd som /matchningar visar (annars: '2 nya' över en vy med 1 rad)");
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

        var failedAdId = SeedActiveAd(db);
        _clock.UtcNow.Returns(T0.AddDays(1));
        var failed = UserJobAdMatch.Create(
            userId, failedAdId, NotifiableMatchGrade.Top, ["csharp"], _clock).Value;
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
