using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Resumes.Improvement;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.ApplyCvImprovements;

/// <summary>
/// Fas 4b PR-7 (#656, ADR 0093 §D2) — the frame-apply command: the ONE server-side write path that
/// turns user-selected, mechanically-verified frame inputs into a new canonical Master content. The
/// handler recomputes the review (compute-on-demand, D8), resolves each finding by SERVER fingerprint
/// (never client-trusted — ADR 0074 Inv. 2), composes the After via <c>FromFrame</c>, runs the shared
/// personnummer guard on the composed content BEFORE it becomes canonical (ADR 0074 Inv. 1), writes
/// once via <c>Resume.UpdateMasterContent</c>, then reconciles the ledger via
/// <c>IResumeReviewReconciler</c> (Fas 4b PR-8.1 fold, CTO-bind Q1) which verdict-verifies each
/// applied criterion to auto-resolve only genuine fixes (CTO D-D). The <see cref="ICvReviewEngine"/>
/// is NSubstitute-mocked (first call = pre-apply review, later calls = the reconciler's post-apply
/// runs); ledger-behavior tests use the REAL reconciler, the rest substitute it. The seeded Resume is
/// loaded TRACKED so its EF-Ignore'd Master content survives (same CLR instance, parity
/// SetFindingStatusCommandHandlerTests).
///
/// CA2012: stubbing the ValueTask-returning ICvReviewEngine.ReviewAsync is the known NSubstitute
/// analyzer false positive. SPEC-DRIVEN — RED until the command + handler ship.
/// </summary>
#pragma warning disable CA2012
public class ApplyCvImprovementsCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();
    private readonly IFrameProvider _frameProvider = Substitute.For<IFrameProvider>();
    private readonly IVerbMapper _verbMapper = Substitute.For<IVerbMapper>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly IResumeReviewReconciler _reconciler = Substitute.For<IResumeReviewReconciler>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly RubricVersion Version = RubricVersion.Parse("1.2.0");

    public ApplyCvImprovementsCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _frameProvider.GetFrameCatalog().Returns(FrameFixtures.Catalog(
            FrameFixtures.SentenceLedde(), FrameFixtures.MeasureAntalPerPeriod()));
        _verbMapper.GetVerbMapping().Returns(
            FrameFixtures.VerbMappingWith("ledde", "skickade", "levererade"));
    }

    private ApplyCvImprovementsCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _frameProvider, _verbMapper, FakeDateTimeProvider.Default, _failedAccess, _reconciler);

    // Fas 4b PR-8.1: the auto-resolve loop folded into the shared reconciler (CTO-bind
    // Q1) — the ledger-behavior tests run the REAL reconciler over the SAME stubbed
    // engine (post-apply stub feeds both profile runs), so PR-7's verdict-verified
    // semantics stay pinned end-to-end through the fold.
    private ApplyCvImprovementsCommandHandler CreateSutWithRealReconciler(
        Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _frameProvider, _verbMapper, FakeDateTimeProvider.Default,
            _failedAccess, new ResumeReviewReconciler(_engine, FakeDateTimeProvider.Default));

    // Pre-apply review, then post-apply verify review (two distinct ReviewAsync calls).
    private void StubReviews(CvReviewResult preApply, CvReviewResult postApply) =>
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(preApply), new ValueTask<CvReviewResult>(postApply));

    private void StubOneReview(CvReviewResult review) =>
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(review));

    private static CvCriterionVerdict Fail(
        string criterionId, string quote, RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(criterionId, category, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(TextSpan.NotLocated, quote.Length, quote), null)]);

    private static CvCriterionVerdict Pass(string criterionId) =>
        CvCriterionVerdict.Assessed(criterionId, RubricCategory.Content, CriterionVerdict.Pass,
            [new TextSpanEvidence(new TextSpan(0, 3, "bra"), null)]);

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(Version, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    private static string Fp(CvCriterionVerdict verdict) => FindingTargetFingerprint.Compute(Version, verdict);

    private const string MeasureLine = FrameFixtures.MeasureLine;

    private static ResumeContent WeakContent() =>
        new(new PersonalInfo("Klas Olsson", "klas@example.se", null, "Stockholm"),
            summary: FrameFixtures.WeakLine);

    private static ResumeContent MeasureContent() =>
        new(new PersonalInfo("Klas Olsson", "klas@example.se", null, "Stockholm"),
            summary: MeasureLine);

    private static FrameApplyInput LeddeChange(string fingerprint) =>
        new("A2", "sentence-ledde", FrameFixtures.LeddeSlots(), fingerprint);

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        ResumeContent? content = null,
        Action<Resume>? configure = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        if (content is not null)
            resume.UpdateMasterContent(content, FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        configure?.Invoke(resume);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    // ===============================================================
    // Happy path — apply + verdict-verified auto-resolve
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldApplyFrameAndAutoResolve_WhenPostApplyVerdictClears()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        var preA2 = Fail("A2", FrameFixtures.WeakLine);
        StubReviews(ResultWith(preA2), ResultWith(Pass("A2")));

        var result = await CreateSutWithRealReconciler(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [LeddeChange(Fp(preA2))]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.MasterVersion.Content.Summary.ShouldBe(FrameFixtures.LeddeAfter);
        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A2");
        row.Status.ShouldBe(ReviewFindingStatus.Resolved);
        row.StaleAt.ShouldBeNull("a freshly auto-resolved finding is not stale.");
    }

    [Fact]
    public async Task Handle_ShouldApplyButNotAutoResolve_WhenPostApplyVerdictStillFails()
    {
        // A partial fix that leaves the criterion Fail/Warn is never auto-resolved — the
        // engine, not the command, decides whether the finding is gone (CTO D-D). Since
        // PR-8.1 the reconcile SEEDS the still-actionable finding as an Open ledger row
        // (the hub badge's "1 att åtgärda"), so "stays Open" is now a literal row.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        var preA2 = Fail("A2", FrameFixtures.WeakLine);
        StubReviews(ResultWith(preA2), ResultWith(Fail("A2", FrameFixtures.LeddeAfter)));

        var result = await CreateSutWithRealReconciler(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [LeddeChange(Fp(preA2))]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        resume.MasterVersion.Content.Summary.ShouldBe(FrameFixtures.LeddeAfter);
        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A2");
        row.Status.ShouldBe(ReviewFindingStatus.Open);
    }

    [Fact]
    public async Task Handle_ShouldStampStaleOnOtherResolvedFinding_AndLeaveTheAppliedFindingFresh()
    {
        // StaleAt coherence (CTO-bind Q2/Q3): the content write invalidates a DIFFERENT resolved
        // finding (B5 → stale), while the applied+cleared criterion (A2) ends Resolved with StaleAt
        // null because the auto-resolve write clears it AFTER the content write.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent(), r =>
            r.SetFindingStatus(Version.ToString(), "B5", ReviewFindingStatus.Resolved,
                new string('a', 64), FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue());
        var preA2 = Fail("A2", FrameFixtures.WeakLine);
        StubReviews(ResultWith(preA2), ResultWith(Pass("A2")));

        var result = await CreateSutWithRealReconciler(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [LeddeChange(Fp(preA2))]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var b5 = resume.FindingStatuses.Single(f => f.CriterionId == "B5");
        var a2 = resume.FindingStatuses.Single(f => f.CriterionId == "A2");
        b5.StaleAt.ShouldNotBeNull("the content change stamps staleness on the other resolved finding.");
        a2.Status.ShouldBe(ReviewFindingStatus.Resolved);
        a2.StaleAt.ShouldBeNull("the applied criterion is re-resolved fresh, so its StaleAt is null.");
    }

    // ===============================================================
    // Frame-validation failures — nothing written
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnFrameUnknown_WhenFrameIdNotInCatalog()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        StubOneReview(ResultWith(Fail("A2", FrameFixtures.WeakLine)));
        var change = new FrameApplyInput("A2", "does-not-exist", FrameFixtures.LeddeSlots(), new string('a', 64));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [change]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameUnknown");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.MasterVersion.Content.Summary.ShouldBe(FrameFixtures.WeakLine);
    }

    [Fact]
    public async Task Handle_ShouldReturnFrameCriterionMismatch_WhenFrameDoesNotRemedyTheCriterion()
    {
        // sentence-ledde remedies A2/C3 only — pointing it at A1 is a client bug.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        StubOneReview(ResultWith(Fail("A1", FrameFixtures.WeakLine)));
        var change = new FrameApplyInput("A1", "sentence-ledde", FrameFixtures.LeddeSlots(), new string('a', 64));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [change]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameCriterionMismatch");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Handle_ShouldReturnFindingChanged_WhenFingerprintDoesNotMatchCurrentFinding()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        StubOneReview(ResultWith(Fail("A2", FrameFixtures.WeakLine)));
        // A stale/forged fingerprint that cannot be the current finding's digest.
        var change = LeddeChange(new string('0', 64));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [change]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingChanged");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        resume.MasterVersion.Content.Summary.ShouldBe(FrameFixtures.WeakLine);
    }

    [Fact]
    public async Task Handle_ShouldReturnFrameSlotNotGrounded_WhenANounSlotIsNotInTheBeforeLine()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        var preA2 = Fail("A2", FrameFixtures.WeakLine);
        StubOneReview(ResultWith(preA2));
        var slots = FrameFixtures.LeddeSlots();
        slots["del4"] = "obefintlig";
        var change = new FrameApplyInput("A2", "sentence-ledde", slots, Fp(preA2));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [change]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameSlotNotGrounded");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.MasterVersion.Content.Summary.ShouldBe(FrameFixtures.WeakLine);
    }

    // ===============================================================
    // Personnummer guard on the composed content (ADR 0074 Inv. 1)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnPersonnummerMustBeRemoved_WhenATextSlotSmugglesAPersonnummer()
    {
        // The free-echo Text slot ("period") lets a personnummer into the composed After; the shared
        // guard runs on the composed content BEFORE it becomes canonical and blocks it — nothing is
        // written. This is the load-bearing reason the guard's arch tripwire covers this handler.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, MeasureContent());
        var preA1 = Fail("A1", MeasureLine);
        StubOneReview(ResultWith(preA1));
        var slots = FrameFixtures.MeasureSlots(verb: "levererade", antal: "30", period: "19811218-9876");
        var change = new FrameApplyInput("A1", "measure-antal-per-period", slots, Fp(preA1));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [change]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");
        resume.MasterVersion.Content.Summary.ShouldBe(MeasureLine, "blocked before any mutation.");
    }

    // ===============================================================
    // Sequential apply — a later change whose line was consumed conflicts
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnConflict_WhenASecondChangeTargetsAnAlreadyConsumedLine()
    {
        // A2 and C3 both cite the SAME weak line; applying the A2 rewrite consumes it, so the C3
        // change can no longer find its line in the already-patched content → Conflict.
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        var preA2 = Fail("A2", FrameFixtures.WeakLine);
        var preC3 = Fail("C3", FrameFixtures.WeakLine, RubricCategory.Language);
        StubReviews(ResultWith(preA2, preC3), ResultWith(Pass("A2"), Pass("C3")));
        var change1 = new FrameApplyInput("A2", "sentence-ledde", FrameFixtures.LeddeSlots(), Fp(preA2));
        var change2 = new FrameApplyInput("C3", "sentence-ledde", FrameFixtures.LeddeSlots(), Fp(preC3));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [change1, change2]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    // ===============================================================
    // Auth / not-found / cross-user (parity SetFindingStatusCommandHandler)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new ApplyCvImprovementsCommandHandler(
            db, anon, _engine, _frameProvider, _verbMapper, FakeDateTimeProvider.Default, _failedAccess, _reconciler);

        await Should.ThrowAsync<UnauthorizedException>(
            () => sut.Handle(
                new ApplyCvImprovementsCommand(Guid.NewGuid(), [LeddeChange(new string('a', 64))]),
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
            () => CreateSut(db).Handle(
                new ApplyCvImprovementsCommand(Guid.NewGuid(), [LeddeChange(new string('a', 64))]),
                CancellationToken.None).AsTask());

        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFoundAndLogCrossUserAttempt_WhenResumeBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
        var otherResume = await SeedResumeAsync(db, Guid.NewGuid(), WeakContent());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(CancellationToken.None);

        await Should.ThrowAsync<NotFoundException>(
            () => CreateSut(db).Handle(
                new ApplyCvImprovementsCommand(otherResume.Id.Value, [LeddeChange(new string('a', 64))]),
                CancellationToken.None).AsTask());

        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, "ApplyCvImprovements");
    }

    // ===============================================================
    // Fas 4b PR-8.1 call-site pin (#657): the frame-apply write path delegates review reconciliation to
    // the reconciler, passing the DISTINCT applied criterion ids as the auto-resolve set (the reconciler
    // re-scores and resolves only what the engine now Passes — CTO D-D). This handler's existing in-line
    // auto-resolve loop + its tests are left intact for now; the implementer folds the loop into the
    // reconciler and rebalances those assertions in the GREEN phase. RED until the handler calls the reconciler.
    // ===============================================================

    [Fact]
    public async Task Handle_OnSuccess_RunsReviewReconcile_WithTheDistinctAppliedCriterionIds()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId, WeakContent());
        var preA2 = Fail("A2", FrameFixtures.WeakLine);
        StubReviews(ResultWith(preA2), ResultWith(Pass("A2")));

        var result = await CreateSut(db).Handle(
            new ApplyCvImprovementsCommand(resume.Id.Value, [LeddeChange(Fp(preA2))]),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _reconciler.Received(1).ReconcileAsync(
            Arg.Is<Resume>(r => r.Id == resume.Id),
            Arg.Is<IReadOnlyCollection<string>>(c => c != null && c.Count == 1 && c.Contains("A2")),
            Arg.Any<CancellationToken>());
    }
}
