using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands;

// Fas 4b CV-motor v2 PR-8.1 (#657, CTO-bind Q1): UpdateMasterContent is the canonical content write
// path, so on success it must run a review reconcile over the new content. The IResumeReviewReconciler
// is threaded as a trailing ctor dependency (added in PR-8.1's skeleton). CA2012: stubbing the
// ValueTask-returning ReconcileAsync is the known NSubstitute analyzer false positive.
#pragma warning disable CA2012
public class UpdateMasterContentCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IResumeReviewReconciler _reconciler = Substitute.For<IResumeReviewReconciler>();
    private readonly Guid _userId = Guid.NewGuid();

    public UpdateMasterContentCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _reconciler.ReconcileAsync(Arg.Any<Resume>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));
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

    private static ResumeContentDto BuildContent(string fullName = "Klas Olsson", string? summary = null) =>
        new(
            new PersonalInfoDto(fullName, "klas@example.se", null, "Stockholm"),
            new List<ExperienceDto>(),
            new List<EducationDto>(),
            new List<SkillDto>(),
            summary);

    [Fact]
    public async Task Handle_WithValidCommand_UpdatesMasterContentAndReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(
            resume.Id.Value,
            BuildContent(summary: "En kort sammanfattning."));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.MasterVersion.Content.Summary.ShouldBe("En kort sammanfattning.");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new UpdateMasterContentCommandHandler(db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(Guid.NewGuid(), BuildContent());

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

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(Guid.NewGuid(), BuildContent());

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var resume = await SeedResumeAsync(db, otherUserId);

        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(resume.Id.Value, BuildContent());

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WithEmptyFullName_ReturnsResumeFullNameRequiredFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        // Domain ValidateContent kontrollerar PersonalInfo.FullName.
        var command = new UpdateMasterContentCommand(resume.Id.Value, BuildContent(fullName: "   "));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
    }

    // ===============================================================
    // Fas 4b PR-4 (ADR 0093 §D2(e), CTO-bind Q2/Q3 + dotnet-architect Viktigt): the
    // staleness stamp lives in Resume.UpdateMasterContent and only fires when the
    // handler has Included the FindingStatuses collection — this test pins that
    // write-path Include contract END-TO-END through the handler, so a future caller
    // (PR-7 apply) or a refactor that drops the Include fails HERE, not silently in
    // production. Resolved goes stale; Ignored (content-independent rule opt-out)
    // does not.
    // ===============================================================

    [Fact]
    public async Task Handle_WithResolvedAndIgnoredFindings_StampsStaleOnlyOnResolved()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var fingerprint = new string('a', 64);
        resume.SetFindingStatus(
            "1.1.0", "A7", ReviewFindingStatus.Resolved, fingerprint, FakeDateTimeProvider.Default)
            .IsSuccess.ShouldBeTrue();
        resume.SetFindingStatus(
            "1.1.0", "C2", ReviewFindingStatus.Ignored, fingerprint, FakeDateTimeProvider.Default)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateMasterContentCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(
            resume.Id.Value, BuildContent(summary: "Uppdaterad sammanfattning."));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var resolved = resume.FindingStatuses.Single(f => f.CriterionId == "A7");
        var ignored = resume.FindingStatuses.Single(f => f.CriterionId == "C2");
        resolved.StaleAt.ShouldNotBeNull(
            "a content change must invalidate a previously-resolved finding in the same transaction");
        ignored.StaleAt.ShouldBeNull(
            "ignorerad is a rule-scoped opt-out that survives content changes (CTO-bind Q3)");
    }

    // ===============================================================
    // #499 (ADR 0074 Invariant 1): UpdateMasterContent ran only structural ValidateContent,
    // so a personnummer typed into the master-edit payload reached an UNFLAGGED canonical
    // Resume (render/PDF). The shared ResumeContentPersonnummerGuard now re-scans the RAW
    // submitted free text (same guard as promote). A hit blocks with Resume.PersonnummerMustBeRemoved
    // and nothing is mutated. The Unicode-dash case proves the merged #497/#498 widening reaches
    // this write surface (regression coupling to PR #520). \u escapes.
    // ===============================================================

    private const string ValidPersonnummer = "811218-9876";

    [Fact]
    public async Task Handle_WhenSubmittedSummaryContainsPersonnummer_ReturnsMustBeRemoved_NoUpdate()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);
        var before = resume.MasterVersion.Content.Summary;

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(
            resume.Id.Value, BuildContent(summary: $"Erfaren utvecklare. Pnr {ValidPersonnummer}."));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");
        resume.MasterVersion.Content.Summary.ShouldBe(before); // blocked before mutation
    }

    [Fact]
    public async Task Handle_WhenPersonnummerWrittenWithUnicodeDash_IsBlocked_WidenedGuardFlowsThrough()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        // EN DASH (U+2013) separator: the merged #497 widening (PR #520) must reach this surface.
        var command = new UpdateMasterContentCommand(
            resume.Id.Value, BuildContent(summary: "Pnr 811218\u20139876."));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");
    }

    [Fact]
    public async Task Handle_WhenPersonnummerInExperienceDescription_IsBlocked_WholeFreeTextScanned()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var content = new ResumeContentDto(
            new PersonalInfoDto("Klas Olsson", "klas@example.se", null, "Stockholm"),
            Experiences:
            [
                new ExperienceDto("Acme AB", "Utvecklare", new DateOnly(2021, 1, 1), null,
                    $"Anstalld, mitt nummer ar {ValidPersonnummer}."),
            ],
            Educations: [],
            Skills: [],
            Summary: null);

        var handler = new UpdateMasterContentCommandHandler(db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(resume.Id.Value, content);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");
    }

    // Fas 4b PR-8.1 call-site pin (#657): on a successful content write the handler runs exactly one
    // review reconcile for the mutated resume, with NO auto-resolve set (a plain master edit re-reviews;
    // only "Åtgärda direkt" passes applied criteria). RED until the handler calls the reconciler.
    [Fact]
    public async Task Handle_OnSuccess_RunsReviewReconcileForTheResume_WithNoAutoResolve()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new UpdateMasterContentCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>(), _reconciler);
        var command = new UpdateMasterContentCommand(resume.Id.Value, BuildContent(summary: "Uppdaterad."));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _reconciler.Received(1).ReconcileAsync(
            Arg.Is<Resume>(r => r.Id == resume.Id),
            Arg.Is<IReadOnlyCollection<string>>(x => x == null),
            Arg.Any<CancellationToken>());
    }
}
