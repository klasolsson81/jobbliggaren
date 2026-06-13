using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.SetResumeLanguage;

public class SetResumeLanguageCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public SetResumeLanguageCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new SetResumeLanguageCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new SetResumeLanguageCommand(resume.Id.Value, "En");

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var reloaded = await db.Resumes.FindAsync([resume.Id], CancellationToken.None);
        reloaded!.Language.ShouldBe(ResumeLanguage.En);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new SetResumeLanguageCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new SetResumeLanguageCommand(Guid.NewGuid(), "Sv");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_ResumeBelongsToOtherUser_ThrowsNotFoundException_LogsCrossUserAttempt()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var resume = await SeedResumeAsync(db, otherUserId);

        // Egen JobSeeker så att jobSeekerId blir != default
        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new SetResumeLanguageCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new SetResumeLanguageCommand(resume.Id.Value, "En");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Resume", resume.Id.Value, _userId, "SetResumeLanguage");
    }

    [Fact]
    public async Task Handle_NonExistentResume_ThrowsNotFoundException_NoLog()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new SetResumeLanguageCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);
        var command = new SetResumeLanguageCommand(Guid.NewGuid(), "En");

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());

        // Okänt id (legitim typo) loggas INTE per IFailedAccessLogger-docs.
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_InvalidLanguage_ReturnsValidationFailure()
    {
        // Försvarsdjup: handler skall returnera DomainError.Validation om någon
        // smiter förbi validator-lagret med okänt språk-namn.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new SetResumeLanguageCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());
        var command = new SetResumeLanguageCommand(resume.Id.Value, "Fr");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.LanguageInvalid");
    }
}
