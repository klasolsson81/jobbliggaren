using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.SetResumeLanguage;

// Fas 4b CV-motor v2 PR-8.1 (#657, CTO-bind Q1): SetResumeLanguage is a canonical write path, so on
// success it must run a review reconcile (findings re-scored for the new language). The
// IResumeReviewReconciler is threaded as a trailing ctor dependency (added in PR-8.1's skeleton).
// CA2012: stubbing the ValueTask-returning ReconcileAsync is the known NSubstitute analyzer false positive.
#pragma warning disable CA2012
public class SetResumeLanguageCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IResumeReviewReconciler _reconciler = Substitute.For<IResumeReviewReconciler>();
    private readonly Guid _userId = Guid.NewGuid();

    public SetResumeLanguageCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _reconciler.ReconcileAsync(Arg.Any<Resume>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));
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
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
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
            db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
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
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger, _reconciler);
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
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger, _reconciler);
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
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new SetResumeLanguageCommand(resume.Id.Value, "Fr");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.LanguageInvalid");
    }

    // Fas 4b PR-8.1 call-site pin (#657): on success the handler runs exactly one review reconcile for
    // the mutated resume, passing NO auto-resolve set (a language change is a plain re-review, not an
    // "Åtgärda direkt" apply). RED until the handler calls the reconciler.
    [Fact]
    public async Task Handle_OnSuccess_RunsReviewReconcileForTheResume_WithNoAutoResolve()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new SetResumeLanguageCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var result = await handler.Handle(
            new SetResumeLanguageCommand(resume.Id.Value, "En"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _reconciler.Received(1).ReconcileAsync(
            Arg.Is<Resume>(r => r.Id == resume.Id),
            Arg.Is<IReadOnlyCollection<string>>(x => x == null),
            Arg.Any<CancellationToken>());
    }
}
