using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Common.Security;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b CV-motor v2 PR-8.1 (issue #657, ADR 0093 §D8; CTO-bind Q1/Q6) — the ResumeReviewReconciler
/// is the ONE server-side unit that recomputes a Resume's review and folds the verdicts into the
/// DEK-free finding-status ledger. It runs the engine for BOTH render profiles (Ats + Visual) over the
/// canonical context, UNIONS the per-criterion verdicts, seeds/refreshes Open rows for the actionable
/// (Fail/Warn) findings via Resume.ReconcileFindingStatuses, and — for the caller-supplied
/// autoResolveCriteria only — flips a criterion the engine now scores Pass to Resolved (the engine,
/// not the click, decides — PR-7 CTO D-D parity). Fingerprints are ALWAYS server-derived
/// (IFindingFingerprinter.Compute — keyed HMAC #692; ADR 0074 Invariant 2).
///
/// The <see cref="ICvReviewEngine"/> is NSubstitute-mocked (one stub per profile); the Resume is a
/// real aggregate so the ledger transitions are exercised end-to-end through the reconciler.
///
/// CA2012: stubbing the ValueTask-returning ICvReviewEngine.ReviewAsync is the known NSubstitute
/// analyzer false positive. SPEC-DRIVEN — RED until IResumeReviewReconciler + ResumeReviewReconciler +
/// Resume.ReconcileFindingStatuses ship.
/// </summary>
#pragma warning disable CA2012
public class ResumeReviewReconcilerTests
{
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();

    private static readonly RubricVersion Version = RubricVersion.Parse("2.2.0");

    private ResumeReviewReconciler CreateSut() =>
        new(_engine, TestFindingFingerprinter.Instance, FakeDateTimeProvider.Default);

    private static Resume CreateResume() =>
        Resume.Create(JobSeekerId.New(), "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;

    private static CvCriterionVerdict Fail(
        string criterionId, string quote = "svag", RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(criterionId, category, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(TextSpan.NotLocated, quote.Length, quote), null)]);

    private static CvCriterionVerdict Warn(
        string criterionId, string quote = "delvis", RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(criterionId, category, CriterionVerdict.Warn,
            [new TextSpanEvidence(new TextSpan(TextSpan.NotLocated, quote.Length, quote), null)]);

    private static CvCriterionVerdict Pass(
        string criterionId, RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(criterionId, category, CriterionVerdict.Pass,
            [new TextSpanEvidence(new TextSpan(0, 3, "bra"), null)]);

    private static CvCriterionVerdict NotAssessed(string criterionId) =>
        CvCriterionVerdict.NotAssessed(criterionId, RubricCategory.Content, "ej bedömt v1");

    private static CvReviewResult ResultFor(RenderProfile profile, params CvCriterionVerdict[] verdicts) =>
        new(Version, profile, [], verdicts, [], verdicts.Length, verdicts.Length);

    // Distinct stub per render profile (the reconciler runs ReviewAsync twice — once per profile).
    // Arg.Is for the profile (not a raw value) so all args are matchers — NSubstitute forbids mixing.
    private void StubProfiles(CvReviewResult ats, CvReviewResult visual)
    {
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Is(RenderProfile.Ats), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(ats));
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Is(RenderProfile.Visual), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(visual));
    }

    // ===============================================================
    // Both profiles + union
    // ===============================================================

    [Fact]
    public async Task ReconcileAsync_RunsEngineForBothProfiles_AndUnionsByCriterionId_IntoOneRow()
    {
        var resume = CreateResume();
        // A1 is a Both-profile criterion returned by BOTH runs — it must union into exactly one row.
        StubProfiles(ResultFor(RenderProfile.Ats, Fail("A1")), ResultFor(RenderProfile.Visual, Fail("A1")));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None);

        await _engine.Received(1).ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Is(RenderProfile.Ats), Arg.Any<CancellationToken>());
        await _engine.Received(1).ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Is(RenderProfile.Visual), Arg.Any<CancellationToken>());
        resume.FindingStatuses.Count(f => f.CriterionId == "A1").ShouldBe(1);
    }

    [Fact]
    public async Task ReconcileAsync_SeedsOpenRows_ForFailAndWarnOnly_NeverForPassOrNotAssessed()
    {
        var resume = CreateResume();
        StubProfiles(
            ResultFor(RenderProfile.Ats, Fail("A1"), Warn("A2"), Pass("B3")),
            ResultFor(RenderProfile.Visual, NotAssessed("E1")));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None);

        resume.FindingStatuses.Select(f => f.CriterionId).OrderBy(x => x).ShouldBe(["A1", "A2"]);
        resume.FindingStatuses.ShouldAllBe(f => f.Status == ReviewFindingStatus.Open);
    }

    [Fact]
    public async Task ReconcileAsync_SeedsVisualOnlyCriterion_FromVisualRun_EvenWhenAtsRunNeverSawIt()
    {
        var resume = CreateResume();
        // E1 (a VisualOnly criterion) only appears on the Visual run; it must still seed a row.
        StubProfiles(
            ResultFor(RenderProfile.Ats),
            ResultFor(RenderProfile.Visual, Fail("E1", category: RubricCategory.VisualQuality)));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None);

        var e1 = resume.FindingStatuses.ShouldHaveSingleItem();
        e1.CriterionId.ShouldBe("E1");
        e1.Status.ShouldBe(ReviewFindingStatus.Open);
    }

    // ===============================================================
    // Server-derived fingerprint + rubric-version propagation
    // ===============================================================

    [Fact]
    public async Task ReconcileAsync_StoresServerDerivedFingerprint_EqualToFindingTargetFingerprintCompute()
    {
        var resume = CreateResume();
        var a1 = Fail("A1", "en svag rad utan siffror");
        StubProfiles(ResultFor(RenderProfile.Ats, a1), ResultFor(RenderProfile.Visual));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None);

        resume.FindingStatuses.ShouldHaveSingleItem()
            .TargetFingerprint.ShouldBe(TestFindingFingerprinter.Compute(Version, a1));
    }

    [Fact]
    public async Task ReconcileAsync_UsesEngineResultRubricVersion_ForRowVersionAndReviewedStamp()
    {
        var resume = CreateResume();
        StubProfiles(ResultFor(RenderProfile.Ats, Fail("A1")), ResultFor(RenderProfile.Visual));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None);

        resume.FindingStatuses.ShouldHaveSingleItem().RubricVersion.ShouldBe("2.2.0");
        resume.ReviewedRubricVersion.ShouldBe("2.2.0");
    }

    // ===============================================================
    // autoResolveCriteria — the engine decides (CTO D-D parity)
    // ===============================================================

    [Fact]
    public async Task ReconcileAsync_WhenAutoResolveCriterionNowPasses_ResolvesWithFreshPassFingerprint()
    {
        var resume = CreateResume();
        var a1Pass = Pass("A1");
        StubProfiles(ResultFor(RenderProfile.Ats, a1Pass), ResultFor(RenderProfile.Visual));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: ["A1"], CancellationToken.None);

        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A1");
        row.Status.ShouldBe(ReviewFindingStatus.Resolved);
        row.TargetFingerprint.ShouldBe(TestFindingFingerprinter.Compute(Version, a1Pass));
    }

    [Fact]
    public async Task ReconcileAsync_WhenAutoResolveCriterionStillFails_StaysOpen_TheEngineDecides()
    {
        var resume = CreateResume();
        StubProfiles(ResultFor(RenderProfile.Ats, Fail("A1")), ResultFor(RenderProfile.Visual));

        // A1 was requested for auto-resolve, but the engine still scores it Fail → it stays Open (a
        // partial fix is honestly not a fix).
        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: ["A1"], CancellationToken.None);

        resume.FindingStatuses.ShouldHaveSingleItem().Status.ShouldBe(ReviewFindingStatus.Open);
    }

    [Fact]
    public async Task ReconcileAsync_WhenAutoResolveIsNull_MakesNoResolveAttempts()
    {
        var resume = CreateResume();
        // A1 passes, but with no autoResolve set nothing is resolved (and a Pass seeds no Open row) —
        // the ledger stays empty.
        StubProfiles(ResultFor(RenderProfile.Ats, Pass("A1")), ResultFor(RenderProfile.Visual));

        await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None);

        resume.FindingStatuses.ShouldNotContain(f => f.Status == ReviewFindingStatus.Resolved);
    }

    // ===============================================================
    // Guard + fail-loud posture (code-reviewer PR-8.1 Minor 3)
    // ===============================================================

    [Fact]
    public async Task ReconcileAsync_WithNullResume_ThrowsArgumentNullException()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await CreateSut().ReconcileAsync(null!, autoResolveCriteria: null, CancellationToken.None));
    }

    [Fact]
    public async Task ReconcileAsync_WhenEngineYieldsMalformedCriterionId_ThrowsInsteadOfClientError()
    {
        // Fail-loud pin (§3 two-idiom rule; dotnet-architect PR-8.1 Minor): every reconcile input is
        // server-derived, so an aggregate-level Result failure (here: a criterion id the engine could
        // never legitimately produce) is a SERVER bug and must surface as an exception (500), never
        // as a Result that would render a 4xx "client error" for a broken invariant.
        var resume = CreateResume();
        StubProfiles(ResultFor(RenderProfile.Ats, Fail("NOT-A-CRITERION")), ResultFor(RenderProfile.Visual));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await CreateSut().ReconcileAsync(resume, autoResolveCriteria: null, CancellationToken.None));
    }
}
