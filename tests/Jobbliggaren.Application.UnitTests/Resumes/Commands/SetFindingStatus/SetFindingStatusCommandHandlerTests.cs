using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Resumes.Review;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.SetFindingStatus;

/// <summary>
/// Fas 4b PR-4 (#653, ADR 0093 §D2(e)) — the command that records the user's decision on one review
/// finding. The fingerprint that identifies the finding instance is SERVER-derived: the handler
/// recomputes the review through the canonical adapter and fingerprints the CURRENT finding (never
/// client-submitted, ADR 0074 Invariant 2). A decision needs a finding to be about — the criterion's
/// current verdict must be Fail or Warn — except reverting to Open, always allowed.
///
/// The <see cref="ICvReviewEngine"/> is NSubstitute-mocked (the handler under test is the
/// orchestration + the actionability guard, not the engine's verdict logic); the REAL
/// <see cref="IRubricProvider"/> resolves the criterion set (unknown-id → NotFound). The seeded
/// Resume is loaded TRACKED, so its EF-Ignore'd Master content survives (same CLR instance) — no
/// hydration interceptor needed, parity with UpdateMasterContentCommandHandlerTests.
///
/// CA2012: stubbing the ValueTask-returning ICvReviewEngine.ReviewAsync is the known NSubstitute
/// analyzer false positive.
/// </summary>
#pragma warning disable CA2012
public class SetFindingStatusCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();
    private readonly IRubricProvider _rubricProvider = CvReviewFixtures.RealRubricProvider();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public SetFindingStatusCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private SetFindingStatusCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _rubricProvider, FakeDateTimeProvider.Default, _failedAccess);

    private void StubEngine(CvReviewResult result) =>
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(result));

    private static readonly RubricVersion Version110 = RubricVersion.Parse("1.1.0");

    private static CvCriterionVerdict Verdict(
        string criterionId, CriterionVerdict verdict, RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(criterionId, category, verdict,
            [new TextSpanEvidence(new TextSpan(0, 6, "driven"), null)]);

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(Version110, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    private static async Task<Resume> SeedResumeAsync(Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    // ===============================================================
    // Happy path — a decision persisted with the server-derived fingerprint
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldPersistResolvedRow_WhenVerdictIsFail()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);
        var verdict = Verdict("A1", CriterionVerdict.Fail);
        StubEngine(ResultWith(verdict));

        var result = await CreateSut(db).Handle(
            new SetFindingStatusCommand(resume.Id.Value, "A1", "Resolved"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A1");
        row.Status.ShouldBe(ReviewFindingStatus.Resolved);
        row.RubricVersion.ShouldBe("1.1.0");
        // The fingerprint is SERVER-derived from the engine's current finding (never client input).
        row.TargetFingerprint.ShouldBe(FindingTargetFingerprint.Compute(Version110, verdict));
    }

    [Fact]
    public async Task Handle_ShouldPersistIgnoredRow_WhenVerdictIsWarn()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);
        StubEngine(ResultWith(Verdict("A7", CriterionVerdict.Warn)));

        var result = await CreateSut(db).Handle(
            new SetFindingStatusCommand(resume.Id.Value, "A7", "Ignored"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.FindingStatuses.ShouldHaveSingleItem().Status.ShouldBe(ReviewFindingStatus.Ignored);
    }

    // ===============================================================
    // Actionability guard (Fail/Warn only; Open always allowed)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnFindingNotActionable_WhenVerdictIsPass()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);
        StubEngine(ResultWith(Verdict("B3", CriterionVerdict.Pass, RubricCategory.Structure)));

        var result = await CreateSut(db).Handle(
            new SetFindingStatusCommand(resume.Id.Value, "B3", "Resolved"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingNotActionable");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldReturnFindingNotActionable_WhenVerdictIsNotAssessed()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);
        var notAssessed = CvCriterionVerdict.NotAssessed("A5", RubricCategory.Content, "ej bedömt v1");
        StubEngine(ResultWith(notAssessed));

        var result = await CreateSut(db).Handle(
            new SetFindingStatusCommand(resume.Id.Value, "A5", "Resolved"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingNotActionable");
        resume.FindingStatuses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldAllowOpenRevert_EvenWhenVerdictIsPass()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);
        StubEngine(ResultWith(Verdict("B3", CriterionVerdict.Pass, RubricCategory.Structure)));

        var result = await CreateSut(db).Handle(
            new SetFindingStatusCommand(resume.Id.Value, "B3", "Open"), CancellationToken.None);

        // Reverting to Open is always allowed so a recorded decision can be withdrawn even after the
        // underlying finding disappeared.
        result.IsSuccess.ShouldBeTrue();
        resume.FindingStatuses.ShouldHaveSingleItem().Status.ShouldBe(ReviewFindingStatus.Open);
    }

    // ===============================================================
    // Unknown criterion — NotFound, engine never reached
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenCriterionIdUnknown()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            new SetFindingStatusCommand(resume.Id.Value, "ZZ99", "Resolved"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        resume.FindingStatuses.ShouldBeEmpty();
        // The criterion is resolved against the rubric BEFORE the engine — no wasted review.
        await _engine.DidNotReceive().ReviewAsync(
            Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Auth / not-found / cross-user
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new SetFindingStatusCommandHandler(
            db, anon, _engine, _rubricProvider, FakeDateTimeProvider.Default, _failedAccess);

        await Should.ThrowAsync<UnauthorizedException>(
            () => sut.Handle(new SetFindingStatusCommand(Guid.NewGuid(), "A1", "Resolved"),
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_WhenResumeNotFound_NoLog()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        await Should.ThrowAsync<NotFoundException>(
            () => CreateSut(db).Handle(new SetFindingStatusCommand(Guid.NewGuid(), "A1", "Resolved"),
                CancellationToken.None).AsTask());

        // An unknown id (legitimate typo) is NOT a cross-user attempt.
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFoundAndLogCrossUserAttempt_WhenResumeBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
        var otherResume = await SeedResumeAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(CancellationToken.None);

        await Should.ThrowAsync<NotFoundException>(
            () => CreateSut(db).Handle(new SetFindingStatusCommand(otherResume.Id.Value, "A1", "Resolved"),
                CancellationToken.None).AsTask());

        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, "SetFindingStatus");
    }
}
