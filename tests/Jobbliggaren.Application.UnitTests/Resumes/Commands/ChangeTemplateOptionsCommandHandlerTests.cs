using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.ChangeTemplateOptions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Events;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands;

// Fas 4b PR-8b 8b.2 (ADR 0096) — the write-path for CV template options. Mirrors
// RenameResumeCommandHandlerTests (owner-scoped, cross-user 404 + logged). Adds the two
// slice-specific invariants: the DPIA-gated photo config is PRESERVED (never touched by
// this write), and the domain no-op on unchanged options returns success WITHOUT an event.
public class ChangeTemplateOptionsCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccessLogger = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public ChangeTemplateOptionsCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private ChangeTemplateOptionsCommandHandler CreateHandler(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _failedAccessLogger);

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        resume.ClearDomainEvents();
        return resume;
    }

    // A non-default, all-four-members-changed command (default is Klar/NavyBlue/Modern/Normal).
    private static ChangeTemplateOptionsCommand NonDefaultCommand(Guid resumeId) =>
        new(resumeId, "MorkPanel", "ForestGreen", "Classic", "Compact");

    [Fact]
    public async Task Handle_WithValidCommand_ChangesOptionsPreservesPhotoAndRaisesEvent()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var result = await CreateHandler(db).Handle(
            NonDefaultCommand(resume.Id.Value), CancellationToken.None);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        resume.TemplateOptions.Template.ShouldBe(CvTemplate.MorkPanel);
        resume.TemplateOptions.AccentColor.ShouldBe(CvAccentColor.ForestGreen);
        resume.TemplateOptions.FontPair.ShouldBe(CvFontPair.Classic);
        resume.TemplateOptions.Density.ShouldBe(CvDensity.Compact);
        // The photo config is untouched by this write (DPIA gate) — still the Default.
        resume.TemplateOptions.PhotoEnabled.ShouldBeFalse();
        resume.TemplateOptions.PhotoShape.ShouldBe(CvPhotoShape.Circle);
        resume.DomainEvents.OfType<ResumeTemplateOptionsChangedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_PreservesPersistedPhotoConfig_WhenChangingVisualMembers()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        // Arrange a non-default photo config via the only mutation path, then change only the
        // visual members through the handler — the command has no photo surface, so the
        // handler must carry the persisted photo forward (Square + enabled).
        resume.ChangeTemplateOptions(
            new CvTemplateOptions(
                CvTemplate.Klar, CvAccentColor.NavyBlue, CvFontPair.Modern,
                CvDensity.Normal, PhotoEnabled: true, CvPhotoShape.Square),
            FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateHandler(db).Handle(
            NonDefaultCommand(resume.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.TemplateOptions.Template.ShouldBe(CvTemplate.MorkPanel);
        resume.TemplateOptions.PhotoEnabled.ShouldBeTrue();
        resume.TemplateOptions.PhotoShape.ShouldBe(CvPhotoShape.Square);
    }

    [Fact]
    public async Task Handle_WhenOptionsUnchanged_ReturnsSuccessWithoutEvent()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        // Default visual members + preserved default photo == current options → no-op.
        var command = new ChangeTemplateOptionsCommand(
            resume.Id.Value, "Klar", "NavyBlue", "Modern", "Normal");

        var result = await CreateHandler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.DomainEvents.OfType<ResumeTemplateOptionsChangedDomainEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithInvalidTemplateName_ReturnsValidationFailure()
    {
        // Defense-in-depth: the validator normally guards, but a direct Send must degrade to
        // a mapped 400, never an unmapped SmartEnum throw.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var command = new ChangeTemplateOptionsCommand(
            resume.Id.Value, "Bogus", "ForestGreen", "Classic", "Compact");

        var result = await CreateHandler(db).Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.TemplateOptionsInvalid");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new ChangeTemplateOptionsCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, _failedAccessLogger);

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(NonDefaultCommand(Guid.NewGuid()), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Should.ThrowAsync<NotFoundException>(
            () => CreateHandler(db).Handle(NonDefaultCommand(Guid.NewGuid()), CancellationToken.None).AsTask());

        // A genuinely non-existent CV is NOT a cross-user attempt — the `if (exists)` guard
        // must keep this out of the audit trail (no false security signal / audit noise).
        _failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_ThrowsNotFoundAndLogsCrossUser()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, Guid.NewGuid()); // owned by another user

        // Own JobSeeker so the caller's jobSeekerId resolves to != default.
        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Should.ThrowAsync<NotFoundException>(
            () => CreateHandler(db).Handle(NonDefaultCommand(resume.Id.Value), CancellationToken.None).AsTask());

        _failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Resume", resume.Id.Value, _userId, "ChangeTemplateOptions");
    }
}
