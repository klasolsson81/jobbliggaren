using JobbPilot.Application.Applications.Commands.TransitionTo;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

public class TransitionToCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public TransitionToCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    [Fact]
    public async Task Handle_DraftToSubmitted_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new TransitionToCommand(app.Id.Value, "Submitted");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_DraftToSubmitted_UpdatesStatusOnAggregate()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
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

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new TransitionToCommand(Guid.NewGuid(), "Submitted");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_InvalidTransition_ReturnsFailureWithCorrectErrorCode()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new TransitionToCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new TransitionToCommand(app.Id.Value, "Accepted");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.InvalidTransition");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new TransitionToCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        var command = new TransitionToCommand(Guid.NewGuid(), "Submitted");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }
}
