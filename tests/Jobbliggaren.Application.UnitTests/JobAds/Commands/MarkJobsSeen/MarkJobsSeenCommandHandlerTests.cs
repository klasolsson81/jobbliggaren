using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Commands.MarkJobsSeen;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — the "markera jobblistan sedd"-command handler. Loads
/// the authenticated user's JobSeeker TRACKED and advances <c>LastSeenJobsAt</c> via the
/// monotonic <see cref="JobSeeker.SetLastSeenJobs"/> (the UnitOfWorkBehavior persists — this
/// test asserts the in-aggregate mutation + the Result, mirroring the MarkMatchesSeen handler
/// shape). Contract:
/// <list type="bullet">
/// <item>no authenticated user → typed Validation failure (<c>JobSeeker.Unauthorized</c>);</item>
/// <item>authenticated but no JobSeeker → typed NotFound failure (<c>JobSeeker.NotFound</c>);</item>
/// <item>happy path → the watermark advances to <c>SeenThrough</c> (#759 / #477 Low 4 — the window
/// the user actually saw, NOT clock-now), and Result.Success;</item>
/// <item>null <c>SeenThrough</c> (empty list / deploy-skew) → falls back to clock-now;</item>
/// <item>future-dated <c>SeenThrough</c> → clamped to now by the aggregate;</item>
/// <item>stale <c>SeenThrough</c> → monotonic no-op, still Success.</item>
/// </list>
/// Typed-error assertions on <see cref="DomainError.Code"/> (not message strings) per the
/// localization-fragility rule.
/// </summary>
public class MarkJobsSeenCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public MarkJobsSeenCommandHandlerTests()
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
        var sut = new MarkJobsSeenCommandHandler(db, UserWith(null), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(Now), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundFailure_WhenNoJobSeekerForUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(Now), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    // #759 (sibling of #477 Low 4) — the watermark advances to the SEEN-THROUGH window (max
    // CreatedAt the user saw), NOT clock-now. An ad ingested after seenThrough (but before this
    // call) stays flagged "Ny".
    [Fact]
    public async Task Handle_ShouldAdvanceWatermarkToSeenThrough_WhenProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);
        seeker.LastSeenJobsAt.ShouldBeNull("precondition: fresh seeker has never seen jobs");

        var seenThrough = Now.AddMinutes(-3); // the newest ad the FE rendered, older than clock-now
        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(seenThrough), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenJobsAt.ShouldBe(seenThrough);
        seeker.LastSeenJobsAt.ShouldNotBe(Now, "the fix: watermark is the seen window, not clock-now");
    }

    // Null SeenThrough (empty ad list / deploy-skew from an older FE) → clock-now fallback.
    [Fact]
    public async Task Handle_ShouldFallBackToClockNow_WhenSeenThroughIsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(null), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenJobsAt.ShouldBe(Now);
    }

    // A future-dated SeenThrough (bad client clock) is clamped to now by the aggregate — it must
    // never push the watermark past reality and silently swallow later ads.
    [Fact]
    public async Task Handle_ShouldClampFutureSeenThroughToNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(Now.AddHours(1)), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenJobsAt.ShouldBe(Now, "future seenThrough is clamped to now");
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
        seeker.SetLastSeenJobs(ahead, _clock);
        db.SaveChanges();
        _clock.UtcNow.Returns(Now);

        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(Now), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenJobsAt.ShouldBe(ahead, "monotonic — a stale call never rewinds the watermark");
    }
}
