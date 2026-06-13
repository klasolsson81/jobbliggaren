using Jobbliggaren.Application.Applications.Commands.CreateApplication;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;


namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

public class CreateApplicationCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public CreateApplicationCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccessWithNonEmptyGuid()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new CreateApplicationCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WithValidCommand_AddsApplicationToDbExactlyOnce()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(null, null);

        await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var count = await db.Applications.CountAsync(CancellationToken.None);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WithJobAdId_SetsJobAdIdOnApplication()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var jobAdId = Guid.NewGuid();
        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(jobAdId, null);

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var app = await db.Applications.FindAsync(
            [new Jobbliggaren.Domain.Applications.ApplicationId(result.Value)],
            TestContext.Current.CancellationToken);
        app.ShouldNotBeNull();
        app!.JobAdId.ShouldNotBeNull();
        app.JobAdId!.Value.Value.ShouldBe(jobAdId);
    }
}
