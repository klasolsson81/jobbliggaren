using JobbPilot.Application.Applications.Commands.AddNote;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

public class AddNoteCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public AddNoteCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAsync(
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

    [Fact]
    public async Task Handle_WithValidContent_ReturnsSuccessWithNonEmptyGuid()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var handler = new AddNoteCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddNoteCommand(app.Id.Value, "Verkar intressant bolag.");

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

        var handler = new AddNoteCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddNoteCommand(Guid.NewGuid(), "En anteckning.");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new AddNoteCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new AddNoteCommand(Guid.NewGuid(), "En anteckning.");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }
}
