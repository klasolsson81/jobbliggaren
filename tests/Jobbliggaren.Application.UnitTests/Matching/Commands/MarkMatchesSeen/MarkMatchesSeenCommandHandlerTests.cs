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
/// this test asserts the in-aggregate mutation + the Result, not SaveChanges, mirroring the
/// SetMatchPreferences handler shape). Contract:
/// <list type="bullet">
/// <item>no authenticated user → typed Validation failure (<c>JobSeeker.Unauthorized</c>);</item>
/// <item>authenticated but no JobSeeker → typed NotFound failure (<c>JobSeeker.NotFound</c>);</item>
/// <item>happy path → the watermark advances to <c>clock.UtcNow</c> and Result.Success.</item>
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

    // =================================================================
    // No authenticated user → Unauthorized failure (no mutation).
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnUnauthorizedFailure_WhenNoAuthenticatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(null), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(), ct);

        result.IsFailure.ShouldBeTrue();
        // Typed error code (localization-stable) — never the Swedish message.
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    // =================================================================
    // Authenticated, no JobSeeker → NotFound failure.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnNotFoundFailure_WhenNoJobSeekerForUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        // No JobSeeker seeded for this user.
        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    // =================================================================
    // Happy path → watermark advances to clock.UtcNow, Result.Success.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldAdvanceWatermarkToNow_WhenJobSeekerExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);
        seeker.LastSeenMatchesAt.ShouldBeNull("precondition: fresh seeker has never seen matches");

        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(), ct);

        result.IsSuccess.ShouldBeTrue();
        // The watermark is advanced to the injected clock (the UnitOfWorkBehavior persists it).
        seeker.LastSeenMatchesAt.ShouldNotBeNull();
        seeker.LastSeenMatchesAt!.Value.ShouldBe(Now);
    }

    [Fact]
    public async Task Handle_ShouldNotMoveWatermarkBackwards_WhenClockIsBeforeExistingWatermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        // Existing watermark in the FUTURE relative to the command clock — the monotonic guard
        // in SetLastSeenMatches must leave it untouched (idempotent / no backwards move), yet
        // the handler still reports Success (the call is harmless, not an error).
        var future = Now.AddHours(1);
        _clock.UtcNow.Returns(future);
        seeker.SetLastSeenMatches(_clock);
        db.SaveChanges();
        _clock.UtcNow.Returns(Now);

        var sut = new MarkMatchesSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkMatchesSeenCommand(), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenMatchesAt!.Value.ShouldBe(future, "monotonic — a stale call never rewinds the watermark");
    }
}
