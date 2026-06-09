using JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

/// <summary>
/// RecordFollowUpOutcomeCommandHandler — paritet med
/// AddFollowUpCommandHandlerTests. RÖD tills handler + command
/// implementerats.
/// </summary>
public class RecordFollowUpOutcomeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public RecordFollowUpOutcomeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application, Guid followUpId)> SeedWithPendingFollowUpAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
        app.AddFollowUp(FollowUpChannel.Email, FakeDateTimeProvider.Default.UtcNow.AddDays(3), null, FakeDateTimeProvider.Default);
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app, app.FollowUps[^1].Id.Value);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app, followUpId) = await SeedWithPendingFollowUpAsync(db, _userId);

        var handler = new RecordFollowUpOutcomeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new RecordFollowUpOutcomeCommand(app.Id.Value, followUpId, "Responded");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenOutcomeAlreadyRecorded_ReturnsConflictFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app, followUpId) = await SeedWithPendingFollowUpAsync(db, _userId);

        var handler = new RecordFollowUpOutcomeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new RecordFollowUpOutcomeCommand(app.Id.Value, followUpId, "Responded");
        await handler.Handle(command, CancellationToken.None);

        var second = await handler.Handle(
            new RecordFollowUpOutcomeCommand(app.Id.Value, followUpId, "NoResponse"),
            CancellationToken.None);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("FollowUp.OutcomeAlreadyRecorded");
    }

    [Fact]
    public async Task Handle_WhenFollowUpNotFound_ReturnsNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app, _) = await SeedWithPendingFollowUpAsync(db, _userId);

        var handler = new RecordFollowUpOutcomeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new RecordFollowUpOutcomeCommand(app.Id.Value, Guid.NewGuid(), "Responded");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotFound");
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordFollowUpOutcomeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new RecordFollowUpOutcomeCommand(Guid.NewGuid(), Guid.NewGuid(), "Responded");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new RecordFollowUpOutcomeCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new RecordFollowUpOutcomeCommand(Guid.NewGuid(), Guid.NewGuid(), "Responded");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenCrossUserApplication_LogsCrossUserAttemptAndThrowsNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        // User A äger ansökan + follow-up.
        var ownerUserId = Guid.NewGuid();
        var (_, ownerApp, ownerFollowUpId) = await SeedWithPendingFollowUpAsync(db, ownerUserId);

        // Aktuell user (annan) har egen JobSeeker men inte ansökan.
        var attacker = JobSeeker.Register(_userId, "Attacker", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(attacker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new RecordFollowUpOutcomeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new RecordFollowUpOutcomeCommand(ownerApp.Id.Value, ownerFollowUpId, "Responded");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application", ownerApp.Id.Value, _userId, "RecordFollowUpOutcome");
    }
}
