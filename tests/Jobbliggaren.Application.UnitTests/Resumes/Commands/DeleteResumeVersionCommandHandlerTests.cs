using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands;

public class DeleteResumeVersionCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public DeleteResumeVersionCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new DeleteResumeVersionCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeVersionCommand(Guid.NewGuid(), Guid.NewGuid());

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

        var handler = new DeleteResumeVersionCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeVersionCommand(Guid.NewGuid(), Guid.NewGuid());

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var otherResume = await SeedResumeAsync(db, otherUserId);

        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new DeleteResumeVersionCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeVersionCommand(
            otherResume.Id.Value,
            otherResume.MasterVersion.Id.Value);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenDeletingMasterVersion_ReturnsResumeMasterCannotBeDeletedFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new DeleteResumeVersionCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeVersionCommand(
            resume.Id.Value,
            resume.MasterVersion.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.MasterCannotBeDeleted");
    }

    [Fact]
    public async Task Handle_WhenVersionIdNotFound_ReturnsResumeVersionNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new DeleteResumeVersionCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new DeleteResumeVersionCommand(resume.Id.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");
    }

    // OBS: Lyckad radering av Tailored-version kan inte testas i Fas 1 eftersom
    // det inte finns någon publik factory på Resume för att skapa en Tailored-version
    // (CreateTailored introduceras först i Fas 4 när AI-pipeline byggs ut).
    // Domain-testet täcker DeleteVersion-flödet direkt på aggregate; här saknas
    // alltså en motsvarande happy-path för handlern tills dess.
}
