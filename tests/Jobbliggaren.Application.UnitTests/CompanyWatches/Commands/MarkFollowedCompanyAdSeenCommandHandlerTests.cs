using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// #453 (cross-channel dedup; ADR 0087 D5-addendum) - the "mark followed-company ad seen" handler.
/// It resolves the UserId from <see cref="ICurrentUser"/> (NEVER the wire), loads the authenticated
/// user's still-Pending, not-yet-seen <c>FollowedCompanyAdHit</c> rows for the given ad TRACKED, and
/// stamps <c>SeenAt</c> via the Pending-only <see cref="FollowedCompanyAdHit.MarkSeen"/> (the
/// UnitOfWorkBehavior persists - these tests assert the in-aggregate mutation + the Result, mirroring
/// <c>MarkMatchesSeenCommandHandlerTests</c>). Contract:
/// <list type="bullet">
/// <item>stamps the current user's Pending hit for the ad (SeenAt = clock.UtcNow);</item>
/// <item>a Queued/Sent hit is never loaded -> never stamped (Pending-only);</item>
/// <item>owner-scoped - ANOTHER user's Pending hit for the SAME ad is untouched (IDOR-safe);</item>
/// <item>no hit for the ad -> benign no-op Success (never NotFound - must not leak follow-existence);</item>
/// <item>unauthenticated -> typed Validation failure (<c>FollowedCompanyAdHit.Unauthorized</c>);</item>
/// <item>the same ad matched via TWO of the user's watches (two hits) -> BOTH stamped.</item>
/// </list>
/// The IAppDbContext is the established real-AppDbContext-over-EF-InMemory fixture
/// (<see cref="TestAppDbContextFactory"/>) so the owner-scoped <c>Where(...)</c> runs as a genuine
/// IQueryable. Typed-error assertions on <see cref="DomainError.Code"/> (localization-stable).
/// </summary>
public class MarkFollowedCompanyAdSeenCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeDateTimeProvider _clock = new(Now);

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private MarkFollowedCompanyAdSeenCommandHandler Handler(AppDbContext db, ICurrentUser user) =>
        new(db, user, _clock);

    // Seeds a hit in the target status (a fresh CompanyWatchId each call so two hits for the same
    // (user, ad) are legal under the UNIQUE (user, ad, watch) key). All seeds start unseen.
    private FollowedCompanyAdHit SeedHit(
        AppDbContext db, Guid userId, JobAdId jobAdId, FollowedCompanyAdHitStatus status)
    {
        var hit = FollowedCompanyAdHit.Create(userId, jobAdId, CompanyWatchId.New(), _clock).Value;
        if (status is FollowedCompanyAdHitStatus.Queued or FollowedCompanyAdHitStatus.Sent)
            hit.MarkQueued();
        if (status == FollowedCompanyAdHitStatus.Sent)
            hit.MarkSent(_clock);
        db.FollowedCompanyAdHits.Add(hit);
        db.SaveChanges();
        return hit;
    }

    [Fact]
    public async Task Handle_ShouldStampCurrentUsersPendingHit_ForGivenJobAd()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        var hit = SeedHit(db, userId, jobAdId, FollowedCompanyAdHitStatus.Pending);
        hit.SeenAt.ShouldBeNull("precondition: a fresh hit has never been seen in-app");

        var result = await Handler(db, UserWith(userId))
            .Handle(new MarkFollowedCompanyAdSeenCommand(jobAdId.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        // Stamped to the injected clock (the UnitOfWorkBehavior persists it in prod).
        hit.SeenAt.ShouldNotBeNull();
        hit.SeenAt!.Value.ShouldBe(Now);
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending, "MarkSeen only stamps");
    }

    [Fact]
    public async Task Handle_ShouldNotStampQueuedOrSentHit_ForGivenJobAd()
    {
        // Pending-only: a post-claim (Queued) or post-send (Sent) hit is never loaded by the handler
        // query, so SeenAt stays null (the email decision is already taken; Sent-dedup covers it).
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        var queued = SeedHit(db, userId, jobAdId, FollowedCompanyAdHitStatus.Queued);
        var sent = SeedHit(db, userId, jobAdId, FollowedCompanyAdHitStatus.Sent);

        var result = await Handler(db, UserWith(userId))
            .Handle(new MarkFollowedCompanyAdSeenCommand(jobAdId.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        queued.SeenAt.ShouldBeNull();
        queued.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued);
        sent.SeenAt.ShouldBeNull();
        sent.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
    }

    [Fact]
    public async Task Handle_ShouldLeaveAnotherUsersPendingHitUntouched_ForSameJobAd()
    {
        // Owner-scope / IDOR-safe: the query filters UserId == currentUser, so user B's Pending hit for
        // the SAME ad is never stamped when user A marks it seen.
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        var hitA = SeedHit(db, userA, jobAdId, FollowedCompanyAdHitStatus.Pending);
        var hitB = SeedHit(db, userB, jobAdId, FollowedCompanyAdHitStatus.Pending);

        var result = await Handler(db, UserWith(userA))
            .Handle(new MarkFollowedCompanyAdSeenCommand(jobAdId.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        hitA.SeenAt.ShouldNotBeNull();
        hitB.SeenAt.ShouldBeNull("another user's hit for the same ad must not be stamped (owner-scope)");
    }

    [Fact]
    public async Task Handle_ShouldReturnSuccessNoOp_WhenNoHitForJobAd()
    {
        // A foreign/absent jobAdId matches zero rows -> benign no-op Success, never NotFound (which would
        // leak whether the user follows this ad's employer). A hit for a DIFFERENT ad stays untouched.
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var otherHit = SeedHit(db, userId, JobAdId.New(), FollowedCompanyAdHitStatus.Pending);

        var result = await Handler(db, UserWith(userId))
            .Handle(new MarkFollowedCompanyAdSeenCommand(Guid.NewGuid()), ct);

        result.IsSuccess.ShouldBeTrue();
        otherHit.SeenAt.ShouldBeNull("a hit for a different ad is not touched");
    }

    [Fact]
    public async Task Handle_ShouldReturnUnauthorizedFailure_WhenUserNotAuthenticated()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();

        var result = await Handler(db, UserWith(null))
            .Handle(new MarkFollowedCompanyAdSeenCommand(Guid.NewGuid()), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.Unauthorized");
    }

    [Fact]
    public async Task Handle_ShouldStampBothHits_WhenAdMatchedViaTwoWatches()
    {
        // The same ad matched via TWO of a user's follows is two hits (the UNIQUE key carries the
        // CompanyWatchId) - the user saw the AD, so both are stamped.
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var jobAdId = JobAdId.New();
        var hit1 = SeedHit(db, userId, jobAdId, FollowedCompanyAdHitStatus.Pending);
        var hit2 = SeedHit(db, userId, jobAdId, FollowedCompanyAdHitStatus.Pending);
        hit1.CompanyWatchId.ShouldNotBe(hit2.CompanyWatchId, "precondition: two distinct watches");

        var result = await Handler(db, UserWith(userId))
            .Handle(new MarkFollowedCompanyAdSeenCommand(jobAdId.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        hit1.SeenAt.ShouldNotBeNull();
        hit2.SeenAt.ShouldNotBeNull();
    }
}
