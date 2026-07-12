using Jobbliggaren.Application.Applications.Commands.DeleteApplication;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

/// <summary>
/// #782 (ADR 0104) — user-initiated per-application HARD delete. Mirrors
/// <see cref="Jobbliggaren.Application.RecentJobSearches.Commands.DeleteRecentSearch"/>:
/// tracked <c>Remove</c> + audit, owner-scoped, cross-user surfaced as an identical
/// NotFound. Physical child-row cascade (FollowUps/Notes/StatusChanges via the DB FK)
/// is asserted in the integration test against a real Postgres.
/// </summary>
public class DeleteApplicationCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public DeleteApplicationCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var application = DomainApplication.Create(
            seeker.Id, jobAdId: null, coverLetter: "Ett personligt brev.",
            manualPosting: null, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(application);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, application);
    }

    [Fact]
    public async Task Handle_WithOwnApplication_HardDeletes()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, application) = await SeedAsync(db, _userId);
        var handler = new DeleteApplicationCommandHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteApplicationCommand(application.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.Applications.Any(a => a.Id == application.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorized()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, application) = await SeedAsync(db, _userId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new DeleteApplicationCommandHandler(
            db, currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteApplicationCommand(application.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId);
        var failedLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new DeleteApplicationCommandHandler(db, _currentUser, failedLogger);

        var result = await handler.Handle(
            new DeleteApplicationCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.NotFound");
        // Unknown id (not a cross-user hit) → no cross-user attempt logged.
        failedLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WithOtherUsersApplication_LogsCrossUserAttempt_AndReturnsNotFound()
    {
        // Two separate seekers — the current user tries to delete the other's application.
        var db = TestAppDbContextFactory.Create();
        var ownerUserId = Guid.NewGuid();
        var (_, application) = await SeedAsync(db, ownerUserId);

        // The current user is different (own seeker so the lookup resolves != default).
        var otherSeeker = JobSeeker.Register(_userId, "Other", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(otherSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new DeleteApplicationCommandHandler(db, _currentUser, failedLogger);

        var result = await handler.Handle(
            new DeleteApplicationCommand(application.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.NotFound");
        failedLogger.Received(1).LogCrossUserAttempt(
            "Application", application.Id.Value, _userId, "DeleteApplication");
        // The target application must NOT have been removed.
        db.Applications.Any(a => a.Id == application.Id).ShouldBeTrue();
    }
}
