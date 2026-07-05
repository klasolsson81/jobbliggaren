using Jobbliggaren.Application.Applications.Commands.TransitionTo;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

public class TransitionToCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public TransitionToCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAsync(
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

    [Fact]
    public async Task Handle_DraftToSubmitted_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new TransitionToCommand(app.Id.Value, "Submitted");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_DraftToSubmitted_UpdatesStatusOnAggregate()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new TransitionToCommand(app.Id.Value, "Submitted");

        await handler.Handle(command, CancellationToken.None);

        var updated = await db.Applications.FindAsync([app.Id], TestContext.Current.CancellationToken);
        updated!.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new TransitionToCommand(Guid.NewGuid(), "Submitted");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenApplicationBelongsToOtherUser_LogsFailedAccessAttempt()
    {
        // TD-67 / ADR 0031: ownership-mismatch loggas via IFailedAccessLogger.
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherApp) = await SeedAsync(db, otherUserId);

        // Egen JobSeeker för current user (annars filtreras inte ut korrekt)
        var ownSeeker = JobSeeker.Register(_userId, "Current User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new TransitionToCommand(otherApp.Id.Value, "Submitted");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application",
            otherApp.Id.Value,
            _userId,
            "TransitionTo");
    }

    [Fact]
    public async Task Handle_WhenApplicationIdUnknown_DoesNotLogFailedAccessAttempt()
    {
        // TD-67 / ADR 0031: okänt id är INTE cross-user-attempt — ska inte logga.
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new TransitionToCommand(Guid.NewGuid(), "Submitted");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_FreeSkipTransition_DraftToAccepted_ReturnsSuccess()
    {
        // ADR 0092 D3: fria övergångar — ett hopp Draft → Accepted som tidigare
        // avvisades (Application.InvalidTransition) lyckas nu via handlern.
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new TransitionToCommand(app.Id.Value, "Accepted");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var updated = await db.Applications.FindAsync([app.Id], TestContext.Current.CancellationToken);
        updated!.Status.ShouldBe(ApplicationStatus.Accepted);
    }

    [Fact]
    public async Task Handle_ManualGhostedFromSubmitted_ReturnsSuccess()
    {
        // ADR 0092 D3: manuell Ghosted routas nu genom TransitionTo-handlern
        // (tidigare blockerad av validatorn "Ghosted sätts automatiskt av systemet").
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);
        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var submit = await handler.Handle(new TransitionToCommand(app.Id.Value, "Submitted"), CancellationToken.None);
        submit.IsSuccess.ShouldBeTrue();

        var result = await handler.Handle(new TransitionToCommand(app.Id.Value, "Ghosted"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var updated = await db.Applications.FindAsync([app.Id], TestContext.Current.CancellationToken);
        updated!.Status.ShouldBe(ApplicationStatus.Ghosted);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new TransitionToCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new TransitionToCommand(Guid.NewGuid(), "Submitted");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }
}
