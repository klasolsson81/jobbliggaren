using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetPrimaryResume;

public class SetPrimaryResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public SetPrimaryResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, Resume resume)> SeedSeekerAndResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, resume);
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var (seeker, resume) = await SeedSeekerAndResumeAsync(db, _userId);

        var handler = new SetPrimaryResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new SetPrimaryResumeCommand(resume.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);

        var reloadedSeeker = await db.JobSeekers.FindAsync([seeker.Id], CancellationToken.None);
        reloadedSeeker!.PrimaryResumeId.ShouldBe(resume.Id);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new SetPrimaryResumeCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new SetPrimaryResumeCommand(Guid.NewGuid());

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_JobSeekerNotFound_ThrowsNotFoundException()
    {
        // currentUser.UserId saknar JobSeeker-rad
        var db = TestAppDbContextFactory.Create();

        var handler = new SetPrimaryResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new SetPrimaryResumeCommand(Guid.NewGuid());

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_ResumeFromOtherUser_ThrowsNotFoundException_LogsCrossUserAttempt()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherResume) = await SeedSeekerAndResumeAsync(db, otherUserId);

        // Egen JobSeeker (utan resumes)
        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new SetPrimaryResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new SetPrimaryResumeCommand(otherResume.Id.Value);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, "SetPrimaryResume");
    }

    [Fact]
    public async Task Handle_NonExistentResume_ThrowsNotFoundException_NoLog()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new SetPrimaryResumeCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new SetPrimaryResumeCommand(Guid.NewGuid());

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }
}
