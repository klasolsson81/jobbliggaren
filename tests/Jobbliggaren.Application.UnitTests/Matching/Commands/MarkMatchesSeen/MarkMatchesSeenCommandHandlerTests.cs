using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Commands.MarkMatchesSeen;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — the "markera matchningarna sedda"-command handler. Loads the
/// authenticated user's JobSeeker TRACKED and advances <c>LastSeenMatchesAt</c> via the
/// monotonic <see cref="JobSeeker.SetLastSeenMatches"/> (the UnitOfWorkBehavior persists —
/// this test asserts the in-aggregate mutation + the Result, not SaveChanges). Contract:
/// <list type="bullet">
/// <item>no authenticated user → typed Validation failure (<c>JobSeeker.Unauthorized</c>);</item>
/// <item>authenticated but no JobSeeker → typed NotFound failure (<c>JobSeeker.NotFound</c>);</item>
/// <item>happy path → the watermark advances to <c>SeenThrough</c> (#477 Low — the window the user
/// actually saw, NOT clock-now), and Result.Success;</item>
/// <item>null <c>SeenThrough</c> (empty list / deploy-skew) → falls back to clock-now.</item>
/// </list>
/// Typed-error assertions on <see cref="DomainError.Code"/> (not message strings) per the
/// localization-fragility rule.
/// </summary>
public class MarkMatchesSeenCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public MarkMatchesSeenCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

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
    public async Task Handle_ShouldReturnUnauthorizedFailure_WhenNoAuthenticatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(null), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(Now), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundFailure_WhenNoJobSeekerForUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(Now), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    // #477 Low — the watermark advances to the SEEN-THROUGH window (max CreatedAt the user saw),
    // NOT clock-now. A match created after seenThrough (but before this call) stays flagged "nya".
    [Fact]
    public async Task Handle_ShouldAdvanceWatermarkToSeenThrough_WhenProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);
        seeker.LastSeenMatchesAt.ShouldBeNull("precondition: fresh seeker has never seen matches");

        var seenThrough = Now.AddMinutes(-3); // the newest match the FE rendered, older than clock-now
        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(seenThrough), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenMatchesAt.ShouldBe(seenThrough);
        seeker.LastSeenMatchesAt.ShouldNotBe(Now, "the fix: watermark is the seen window, not clock-now");
    }

    // Null SeenThrough (empty match list / deploy-skew from an older FE) → clock-now fallback.
    [Fact]
    public async Task Handle_ShouldFallBackToClockNow_WhenSeenThroughIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(null), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenMatchesAt.ShouldBe(Now);
    }

    // A future-dated SeenThrough (bad client clock) is clamped to now by the aggregate — it must
    // never push the watermark past reality and silently swallow later matches.
    [Fact]
    public async Task Handle_ShouldClampFutureSeenThroughToNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(Now.AddHours(1)), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenMatchesAt.ShouldBe(Now, "future seenThrough is clamped to now");
    }

    [Fact]
    public async Task Handle_ShouldNotMoveWatermarkBackwards_WhenSeenThroughIsBeforeExistingWatermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        // Existing watermark ahead of the incoming seenThrough — the monotonic guard must leave it
        // untouched (idempotent / no backwards move), yet the handler still reports Success.
        var ahead = Now.AddHours(1);
        _clock.UtcNow.Returns(ahead);
        seeker.SetLastSeenMatches(ahead, _clock);
        db.SaveChanges();
        _clock.UtcNow.Returns(Now);

        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(Now), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenMatchesAt.ShouldBe(ahead, "monotonic — a stale call never rewinds the watermark");
    }
}
