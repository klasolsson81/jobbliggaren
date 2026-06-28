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
/// <item>happy path → the watermark advances to <c>clock.UtcNow</c> and Result.Success;</item>
/// <item>stale clock → monotonic no-op, still Success.</item>
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

        var result = await sut.Handle(new MarkJobsSeenCommand(), ct);

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

        var result = await sut.Handle(new MarkJobsSeenCommand(), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_ShouldAdvanceWatermarkToNow_WhenJobSeekerExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);
        seeker.LastSeenJobsAt.ShouldBeNull("precondition: fresh seeker has never seen jobs");

        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenJobsAt.ShouldNotBeNull();
        seeker.LastSeenJobsAt!.Value.ShouldBe(Now);
    }

    [Fact]
    public async Task Handle_ShouldNotMoveWatermarkBackwards_WhenClockIsBeforeExistingWatermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);

        var future = Now.AddHours(1);
        _clock.UtcNow.Returns(future);
        seeker.SetLastSeenJobs(_clock);
        db.SaveChanges();
        _clock.UtcNow.Returns(Now);

        var sut = new MarkJobsSeenCommandHandler(db, UserWith(userId), _clock);

        var result = await sut.Handle(new MarkJobsSeenCommand(), ct);

        result.IsSuccess.ShouldBeTrue();
        seeker.LastSeenJobsAt!.Value.ShouldBe(future, "monotonic — a stale call never rewinds the watermark");
    }
}
