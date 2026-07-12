using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands.SetLastSeenFollowedAds;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands.SetLastSeenFollowedAds;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — the follow-rail "seen"-command handler. Loads the authenticated
/// user's JobSeeker TRACKED and advances <c>LastSeenFollowedAdsAt</c> via the monotonic
/// <see cref="JobSeeker.SetLastSeenFollowedAds"/> (the UnitOfWorkBehavior persists; this test asserts
/// the in-aggregate mutation + the Result). Mirrors <c>MarkMatchesSeenCommandHandlerTests</c>:
/// <list type="bullet">
/// <item>no authenticated user → typed Validation failure (<c>JobSeeker.Unauthorized</c>);</item>
/// <item>authenticated but no JobSeeker → typed NotFound failure (<c>JobSeeker.NotFound</c>);</item>
/// <item>happy path → the watermark advances to <c>SeenThrough</c>, Result.Success;</item>
/// <item>null <c>SeenThrough</c> (the follows hub sends no window) → clock-now fallback;</item>
/// <item>future <c>SeenThrough</c> → clamped to now by the aggregate.</item>
/// </list>
/// Typed-error assertions on <see cref="DomainError.Code"/> (not message strings).
/// </summary>
public class SetLastSeenFollowedAdsCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public SetLastSeenFollowedAdsCommandHandlerTests() => _clock.UtcNow.Returns(Now);

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private JobSeeker SeedSeeker(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);
        db.SaveChanges();
        return seeker;
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorizedFailure_WhenNoAuthenticatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var sut = new SetLastSeenFollowedAdsCommandHandler(db, UserWith(null), _clock);

        var result = await sut.Handle(new SetLastSeenFollowedAdsCommand(Now), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundFailure_WhenNoJobSeekerForUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var sut = new SetLastSeenFollowedAdsCommandHandler(db, UserWith(Guid.NewGuid()), _clock);

        var result = await sut.Handle(new SetLastSeenFollowedAdsCommand(Now), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_AdvancesWatermarkToSeenThrough_WhenProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);
        seeker.LastSeenFollowedAdsAt.ShouldBeNull("precondition: fresh seeker has never seen the rail");

        var seenThrough = Now.AddMinutes(-3);
        var sut = new SetLastSeenFollowedAdsCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new SetLastSeenFollowedAdsCommand(seenThrough), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenFollowedAdsAt.ShouldBe(seenThrough);
    }

    [Fact]
    public async Task Handle_FallsBackToClockNow_WhenSeenThroughIsNull()
    {
        // The follows hub renders no individual hits to preserve → the FE sends no window → clock-now.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var sut = new SetLastSeenFollowedAdsCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new SetLastSeenFollowedAdsCommand(null), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenFollowedAdsAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Handle_ClampsFutureSeenThroughToNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var sut = new SetLastSeenFollowedAdsCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new SetLastSeenFollowedAdsCommand(Now.AddHours(1)), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenFollowedAdsAt.ShouldBe(Now, "future seenThrough is clamped to now");
    }
}
