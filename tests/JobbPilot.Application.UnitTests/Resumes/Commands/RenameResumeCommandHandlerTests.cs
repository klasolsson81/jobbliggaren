using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.Resumes.Commands.RenameResume;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Resumes.Commands;

public class RenameResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public RenameResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        string name = "Original")
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, name, "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return resume;
    }

    [Fact]
    public async Task Handle_WithValidCommand_RenamesResumeAndReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new RenameResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new RenameResumeCommand(resume.Id.Value, "Nytt namn");

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var reloaded = await db.Resumes.FindAsync(
            [resume.Id], TestContext.Current.CancellationToken);
        reloaded!.Name.ShouldBe("Nytt namn");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new RenameResumeCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        var command = new RenameResumeCommand(Guid.NewGuid(), "Nytt namn");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new RenameResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new RenameResumeCommand(Guid.NewGuid(), "Nytt namn");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var resume = await SeedResumeAsync(db, otherUserId);

        // Egen JobSeeker så att jobSeekerId blir != default
        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new RenameResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new RenameResumeCommand(resume.Id.Value, "Nytt namn");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WithEmptyName_ReturnsResumeNameRequiredFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new RenameResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new RenameResumeCommand(resume.Id.Value, "   ");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }
}
