using Jobbliggaren.Application.Applications.Commands.AddFollowUp;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

public class AddFollowUpCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public AddFollowUpCommandHandlerTests()
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
