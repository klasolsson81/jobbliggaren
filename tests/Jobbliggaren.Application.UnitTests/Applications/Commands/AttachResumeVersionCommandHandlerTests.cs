using Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

// RÖD svit (TDD) — F4-11 AttachResumeVersionCommandHandler.
//
// Mönster speglar TransitionToCommandHandlerTests (riktig AppDbContext via
// TestAppDbContextFactory.Create() = EF InMemory, seed via JobSeeker.Register +
// DomainApplication.Create + Resume.Create), men assertions följer den
// CTO-bundna Result-formen för AttachResumeVersion: handlern RETURNERAR
// Result.Failure (DomainError) i stället för att kasta — parity med
// CreateApplicationCommandHandler, INTE TransitionTo (som kastar). Se rapport.
//
// IDOR-gränsen (versionen tillhör annan users Resume) är det viktigaste testet:
// handlern måste logga LogCrossUserAttempt("ResumeVersion", ...) + returnera
// NotFound utan att mutera ansökan.
public class AttachResumeVersionCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public AttachResumeVersionCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedSeekerAndAppAsync(
        Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    private static async Task<Resume> SeedResumeForAsync(
        Infrastructure.Persistence.AppDbContext db,
        JobSeekerId jobSeekerId)
    {
        var resume = Resume.Create(jobSeekerId, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    private static AttachResumeVersionCommandHandler CreateHandler(
        Infrastructure.Persistence.AppDbContext db,
        ICurrentUser currentUser,
        IFailedAccessLogger? failedAccessLogger = null) =>
        new(db, currentUser, FakeDateTimeProvider.Default,
            failedAccessLogger ?? Substitute.For<IFailedAccessLogger>());

    // ---------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenUserOwnsAppAndVersion_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (seeker, app) = await SeedSeekerAndAppAsync(db, _userId);
        var resume = await SeedResumeForAsync(db, seeker.Id);
        var versionId = resume.MasterVersion.Id;

        var handler = CreateHandler(db, _currentUser);
        var command = new AttachResumeVersionCommand(app.Id.Value, versionId.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserOwnsAppAndVersion_SetsResumeVersionIdOnAggregate()
    {
        var db = TestAppDbContextFactory.Create();
        var (seeker, app) = await SeedSeekerAndAppAsync(db, _userId);
        var resume = await SeedResumeForAsync(db, seeker.Id);
        var versionId = resume.MasterVersion.Id;

        var handler = CreateHandler(db, _currentUser);
        var command = new AttachResumeVersionCommand(app.Id.Value, versionId.Value);

        await handler.Handle(command, CancellationToken.None);

        var updated = await db.Applications.FindAsync([app.Id], TestContext.Current.CancellationToken);
        updated!.ResumeVersionId.ShouldBe(versionId);
    }

    // ---------------------------------------------------------------
    // Auth + JobSeeker-uppslag
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = CreateHandler(db, currentUser);
        var command = new AttachResumeVersionCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create();
        // Ingen JobSeeker seedad för current user.
        var handler = CreateHandler(db, _currentUser);
        var command = new AttachResumeVersionCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    // ---------------------------------------------------------------
    // Application-ownership (cross-user → logg + NotFound)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenApplicationBelongsToOtherUser_LogsCrossUserAttemptAndReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherApp) = await SeedSeekerAndAppAsync(db, otherUserId);

        // Egen JobSeeker för current user (annars stoppas vi tidigare på JobSeeker.NotFound).
        var ownSeeker = JobSeeker.Register(_userId, "Current User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, _currentUser, failedAccessLogger);
        var command = new AttachResumeVersionCommand(otherApp.Id.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.NotFound");
        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application",
            otherApp.Id.Value,
            _userId,
            "AttachResumeVersion");
    }

    [Fact]
    public async Task Handle_WhenApplicationIdUnknown_DoesNotLogCrossUserAttempt()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = JobSeeker.Register(_userId, "Current User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, _currentUser, failedAccessLogger);
        var command = new AttachResumeVersionCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.NotFound");
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            "Application", Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------
    // ResumeVersion-ownership — IDOR-gränsen (viktigaste testet)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenVersionBelongsToAnotherUsersResume_LogsCrossUserAttemptAndReturnsNotFound()
    {
        // IDOR: current user äger ansökan, men det angivna versionId finns ENBART
        // under en ANNAN users Resume. Handlern måste neka + logga
        // LogCrossUserAttempt("ResumeVersion", ...) + INTE mutera ansökan.
        var db = TestAppDbContextFactory.Create();
        var (mySeeker, myApp) = await SeedSeekerAndAppAsync(db, _userId);

        var otherUserId = Guid.NewGuid();
        var otherSeeker = JobSeeker.Register(otherUserId, "Other User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(otherSeeker);
        await db.SaveChangesAsync(CancellationToken.None);
        var otherResume = await SeedResumeForAsync(db, otherSeeker.Id);
        var otherVersionId = otherResume.MasterVersion.Id;

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, _currentUser, failedAccessLogger);
        var command = new AttachResumeVersionCommand(myApp.Id.Value, otherVersionId.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");
        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "ResumeVersion",
            otherVersionId.Value,
            _userId,
            "AttachResumeVersion");

        var unchanged = await db.Applications.FindAsync([myApp.Id], TestContext.Current.CancellationToken);
        unchanged!.ResumeVersionId.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenVersionDoesNotExistAnywhere_ReturnsNotFoundWithoutCrossUserLog()
    {
        var db = TestAppDbContextFactory.Create();
        var (mySeeker, myApp) = await SeedSeekerAndAppAsync(db, _userId);
        // Seeda EN egen Resume så att uppslaget mot egna versioner är icke-tomt,
        // men använd ett versionId som inte finns någonstans.
        await SeedResumeForAsync(db, mySeeker.Id);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, _currentUser, failedAccessLogger);
        var command = new AttachResumeVersionCommand(myApp.Id.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            "ResumeVersion", Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------
    // Domän-fel propageras (terminal status, ägd app + ägd version)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenApplicationInTerminalStatus_ReturnsDomainFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        // Bygg en Accepted-ansökan (terminal) — transitions via aggregatet.
        var app = DomainApplication.Create(seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.InterviewScheduled, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.Interviewing, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.OfferReceived, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.Accepted, FakeDateTimeProvider.Default);
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var resume = await SeedResumeForAsync(db, seeker.Id);
        var versionId = resume.MasterVersion.Id;

        var handler = CreateHandler(db, _currentUser);
        var command = new AttachResumeVersionCommand(app.Id.Value, versionId.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.ResumeVersionAttachNotAllowed");
    }
}
