using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.SavedSearches.Commands.MarkResultsSeen;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Commands.MarkResultsSeen;

/// <summary>
/// #312 (ADR 0115) — the "markera sparad-söknings-resultat sedda"-command handler. Loads the
/// caller's OWN SavedSearch TRACKED and advances <c>SavedSearch.ResultsSeenAt</c> via the monotonic
/// <see cref="SavedSearch.MarkResultsSeen"/> (the UnitOfWorkBehavior persists — this test asserts
/// the in-aggregate mutation + the Result). Contract: unauthenticated → Validation; no JobSeeker /
/// unknown id / cross-tenant → NotFound (cross-tenant additionally logged, no existence leak);
/// happy → the watermark advances to <c>SeenThrough</c> (#477); null → clock-now; future → clamped;
/// earlier → monotonic no-op. Typed-error assertions on <see cref="Jobbliggaren.Domain.Common.DomainError.Code"/>.
/// </summary>
public class MarkSavedSearchResultsSeenCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 9, 0, 0, TimeSpan.Zero);
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IFailedAccessLogger _failedAccessLogger = Substitute.For<IFailedAccessLogger>();

    public MarkSavedSearchResultsSeenCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private static SearchCriteria Criteria() =>
        SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: null, region: null,
            employmentType: null, worktimeExtent: null, employer: null, remote: false,
            q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value;

    // Seeds a JobSeeker + one notification-enabled SavedSearch created at Now (so ResultsSeenAt
    // baselines at Now). Time advances in the tests that need advancement past the baseline.
    private (JobSeeker seeker, SavedSearch saved) Seed(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);
        var saved = SavedSearch.Create(seeker.Id, "Backend", Criteria(), true, _clock).Value;
        db.SavedSearches.Add(saved);
        db.SaveChanges();
        return (seeker, saved);
    }

    private MarkSavedSearchResultsSeenCommandHandler Sut(AppDbContext db, ICurrentUser user) =>
        new(db, user, _failedAccessLogger, _clock);

    [Fact]
    public async Task Handle_ShouldReturnUnauthorized_WhenNoAuthenticatedUser()
    {
        using var db = TestAppDbContextFactory.Create();

        var result = await Sut(db, UserWith(null)).Handle(
            new MarkSavedSearchResultsSeenCommand(Guid.NewGuid(), Now), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.Unauthorized");
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenNoJobSeekerForUser()
    {
        using var db = TestAppDbContextFactory.Create();

        var result = await Sut(db, UserWith(Guid.NewGuid())).Handle(
            new MarkSavedSearchResultsSeenCommand(Guid.NewGuid(), Now), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");
    }

    [Fact]
    public async Task Handle_ShouldAdvanceWatermarkToSeenThrough_WhenOwnedAndProvided()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var (_, saved) = Seed(db, userId);
        _clock.UtcNow.Returns(Now.AddHours(1));        // time has passed since creation
        var seenThrough = Now.AddMinutes(30);          // a new ad's CreatedAt, > baseline, < now

        var result = await Sut(db, UserWith(userId)).Handle(
            new MarkSavedSearchResultsSeenCommand(saved.Id.Value, seenThrough), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        saved.ResultsSeenAt.ShouldBe(seenThrough);     // the seen window, not clock-now (#477)
    }

    [Fact]
    public async Task Handle_ShouldFallBackToClockNow_WhenSeenThroughIsNull()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var (_, saved) = Seed(db, userId);
        var later = Now.AddHours(1);
        _clock.UtcNow.Returns(later);

        var result = await Sut(db, UserWith(userId)).Handle(
            new MarkSavedSearchResultsSeenCommand(saved.Id.Value, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        saved.ResultsSeenAt.ShouldBe(later);
    }

    [Fact]
    public async Task Handle_ShouldClampFutureSeenThroughToNow()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var (_, saved) = Seed(db, userId);
        var now = Now.AddHours(2);
        _clock.UtcNow.Returns(now);

        var result = await Sut(db, UserWith(userId)).Handle(
            new MarkSavedSearchResultsSeenCommand(saved.Id.Value, now.AddHours(5)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        saved.ResultsSeenAt.ShouldBe(now, "future seenThrough is clamped to now, never past reality");
    }

    [Fact]
    public async Task Handle_ShouldNotRewindWatermark_WhenSeenThroughIsBeforeExisting()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var (_, saved) = Seed(db, userId);
        var ahead = Now.AddHours(2);
        _clock.UtcNow.Returns(ahead);
        saved.MarkResultsSeen(ahead, _clock);
        db.SaveChanges();
        _clock.UtcNow.Returns(Now.AddHours(3));        // no clamp on the incoming (earlier) value

        var result = await Sut(db, UserWith(userId)).Handle(
            new MarkSavedSearchResultsSeenCommand(saved.Id.Value, Now.AddHours(1)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        saved.ResultsSeenAt.ShouldBe(ahead, "monotonic — a stale call never rewinds the watermark");
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundAndNotLog_WhenIdUnknown()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        Seed(db, userId);

        var result = await Sut(db, UserWith(userId)).Handle(
            new MarkSavedSearchResultsSeenCommand(Guid.NewGuid(), Now), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");
        _failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundAndLog_WhenBelongsToOtherUser()
    {
        using var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = Seed(db, otherUserId);
        // Register the CURRENT user's own seeker so the miss is a genuine cross-tenant access
        // (jobSeekerId lookup succeeds), not a "no seeker" miss.
        var userId = Guid.NewGuid();
        db.JobSeekers.Add(JobSeeker.Register(userId, "Current", _clock).Value);
        db.SaveChanges();

        var result = await Sut(db, UserWith(userId)).Handle(
            new MarkSavedSearchResultsSeenCommand(otherSaved.Id.Value, Now), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");   // no existence leak
        _failedAccessLogger.Received(1).LogCrossUserAttempt(
            "SavedSearch", otherSaved.Id.Value, userId, "MarkSavedSearchResultsSeen");
        otherSaved.ResultsSeenAt.ShouldBe(Now, "the other user's watermark is untouched");
    }
}
