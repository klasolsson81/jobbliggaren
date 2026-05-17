using JobbPilot.Application.Applications.Commands.AddFollowUp;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

public class AddFollowUpCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public AddFollowUpCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedDraftAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
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
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, null, FakeDateTimeProvider.Default).Value;
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.InterviewScheduled, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.Interviewing, FakeDateTimeProvider.Default);
        app.TransitionTo(ApplicationStatus.OfferReceived, FakeDateTimeProvider.Default);
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

        var handler = new AddFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddFollowUpCommand(
            app.Id.Value,
            "Email",
            FakeDateTimeProvider.Default.UtcNow.AddDays(3),
            null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new AddFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddFollowUpCommand(
            Guid.NewGuid(),
            "Email",
            FakeDateTimeProvider.Default.UtcNow.AddDays(3),
            null);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenApplicationIsAccepted_ReturnsFailureWithFollowUpNotAllowed()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAcceptedAsync(db, _userId);

        var handler = new AddFollowUpCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddFollowUpCommand(
            app.Id.Value,
            "Email",
            FakeDateTimeProvider.Default.UtcNow.AddDays(3),
            null);

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

        var handler = new AddFollowUpCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddFollowUpCommand(
            Guid.NewGuid(),
            "Email",
            FakeDateTimeProvider.Default.UtcNow.AddDays(3),
            null);

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }
}
