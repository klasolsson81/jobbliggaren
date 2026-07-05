using Jobbliggaren.Application.Applications.Commands.LogFollowUp;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

/// <summary>
/// LogFollowUpCommandHandler — ADR 0092 D4/D5 "Logga uppföljning". Owner-scoped,
/// cross-user → LogCrossUserAttempt + NotFound. Paritet med
/// AddFollowUpCommandHandlerTests / RecordFollowUpOutcomeCommandHandlerTests.
/// </summary>
public class LogFollowUpCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public LogFollowUpCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedDraftAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAcceptedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
        // ADR 0092 D3 — free transitions: reach a terminal (closed-for-activity) directly.
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.Accepted, FakeDateTimeProvider.Default);
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccessWithNonEmptyGuid()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedDraftAsync(db, _userId);

        var handler = new LogFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new LogFollowUpCommand(app.Id.Value, "Ringde rekryteraren");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WithNullNote_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedDraftAsync(db, _userId);

        var handler = new LogFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new LogFollowUpCommand(app.Id.Value, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new LogFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new LogFollowUpCommand(Guid.NewGuid(), "Kontakt");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenApplicationIsAccepted_ReturnsFailureWithFollowUpNotAllowed()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAcceptedAsync(db, _userId);

        var handler = new LogFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new LogFollowUpCommand(app.Id.Value, "Kontakt");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotAllowed");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new LogFollowUpCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new LogFollowUpCommand(Guid.NewGuid(), "Kontakt");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenCrossUserApplication_LogsCrossUserAttemptAndThrowsNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        // User A owns the application.
        var ownerUserId = Guid.NewGuid();
        var (_, ownerApp) = await SeedDraftAsync(db, ownerUserId);

        // The current (different) user has their own JobSeeker but not this application.
        var attacker = JobSeeker.Register(_userId, "Attacker", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(attacker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new LogFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new LogFollowUpCommand(ownerApp.Id.Value, "Kontakt");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application", ownerApp.Id.Value, _userId, "LogFollowUp");
    }
}
