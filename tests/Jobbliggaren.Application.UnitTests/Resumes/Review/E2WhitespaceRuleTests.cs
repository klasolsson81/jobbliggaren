using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-6b (#655, ADR 0093 §D4) — E2 "Whitespace", the ONE geometry signal a PDF gives
/// honestly: the tightest page margin. Driven through the REAL <see cref="CvReviewEngine"/>
/// (all-correct spell-checker stub so C7 never interferes). E2 is a VisualOnly criterion → it
/// appears ONLY in a Visual-profile review. The load-bearing honesty property (parity B5): E2
/// NEVER reports Pass — a healthy margin proves neither line spacing nor section spacing, so:
///   • tightest margin &lt; floor(~1 cm) → Warn (cramped);
///   • tightest margin ≥ floor → NotAssessed (margins ok, but the rest of the whitespace
///     criterion is unmeasured — the honest ceiling, never a Pass);
///   • no margin (null, a Failed/NotApplicable parse, the canonical arm) → NotAssessed.
/// The floor is rubric v2.1 DATA (thresholds.minMarginPointsFloor), derived from the real asset.
/// </summary>
public class E2WhitespaceRuleTests
{
    private static CvReviewEngine Engine(IRubricProvider? rubric = null) =>
        new(rubric ?? RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> E2Async(CvLayoutMetrics? layout, IRubricProvider? rubric = null)
    {
        // E2 is VisualOnly — it is only emitted in the Visual profile.
        var result = await Engine(rubric).ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: layout)),
            RenderProfile.Visual, TestContext.Current.CancellationToken);
        return Verdict(result, "E2");
    }

    // The cramped-margin floor (PDF points) as versioned DATA (never a literal in the test).
    private static double Floor() =>
        RealRubric().Criteria.Single(c => c.Id == "E2")
            .RequiredThreshold(RubricThresholdKeys.MinMarginPointsFloor);

    // ===============================================================
    // Warn — a cramped margin below the floor; cites a structural note
    // ===============================================================

    [Fact]
    public async Task E2_ShouldWarn_WhenTheTightestMarginIsBelowTheFloor()
    {
        var cramped = Floor() - 5.0; // clearly under ~1 cm
        var e2 = await E2Async(CvLayoutMetrics.Analyzed(fileSizeBytes: 200_000, pageCount: 1, minMarginPoints: cramped));

        e2.Verdict.ShouldBe(CriterionVerdict.Warn);
        e2.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldNotBeNullOrWhiteSpace();
    }

    // ===============================================================
    // NotAssessed (NEVER Pass) — margins ok but the rest is unmeasured
    // ===============================================================

    [Fact]
    public async Task E2_ShouldNotAssessAndNeverPass_WhenTheMarginIsAtOrAboveTheFloor()
    {
        var roomy = Floor() + 20.0;
        var e2 = await E2Async(CvLayoutMetrics.Analyzed(200_000, 1, roomy));

        e2.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            "en rimlig marginal bevisar inte radavstånd/luft — ärligt tak, aldrig Pass.");
        e2.Verdict.ShouldNotBe(CriterionVerdict.Pass, "E2 rapporterar ALDRIG Pass (paritet B5).");
        e2.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
        AssertTheMarginsReadPair(e2);
    }

    [Fact]
    public async Task E2_ShouldNotAssess_WhenTheMarginIsExactlyTheFloor()
    {
        // Strict boundary: margin < floor warns, so exactly-at-floor is NotAssessed (not Warn).
        var e2 = await E2Async(CvLayoutMetrics.Analyzed(200_000, 1, Floor()));

        e2.Verdict.ShouldBe(CriterionVerdict.NotAssessed, "vid golvet är marginalen inte < golvet → inte Warn.");
        e2.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        AssertTheMarginsReadPair(e2);
    }

    // ===============================================================
    // NotAssessed — no margin signal (null / Failed / NotApplicable / no layout)
    // ===============================================================

    [Fact]
    public async Task E2_ShouldNotAssess_WhenTheMarginCouldNotBeLocated()
    {
        // Analyzed but no page carried locatable text → null margin → NotAssessed.
        var e2 = await E2Async(CvLayoutMetrics.Analyzed(200_000, 1, minMarginPoints: null));

        e2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        e2.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        AssertTheRubricTextOnlyPair(e2);
    }

    [Fact]
    public async Task E2_ShouldNotAssess_WhenLayoutMetricsAreAbsent()
    {
        var e2 = await E2Async(layout: null);

        e2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        e2.Evidence.ShouldBeEmpty();
        e2.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
        AssertTheRubricTextOnlyPair(e2);
    }

    [Fact]
    public async Task E2_ShouldNotAssess_WhenTheFileIsADocx()
    {
        // NotApplicable geometry (a DOCX) has no margin → E2 NotAssessed, never Pass.
        var e2 = await E2Async(CvLayoutMetrics.NotApplicable(fileSizeBytes: 350_000));

        e2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        e2.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        AssertTheRubricTextOnlyPair(e2);
    }

    [Fact]
    public async Task E2_ShouldUseItsOwnFallbackPair_WhenNoMarginWasReadAndTheAssetPredatesTheKind()
    {
        // The shipped asset as it stood before #2083 (reason, no kind): the rubric reason is never
        // shown beside a guessed kind, so E2's own no-margin code pair is used whole (CTO F2).
        var e2 = await E2Async(layout: null, RubricPredatingNotAssessedKind());

        e2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        e2.NotAssessedKind.ShouldBe(NotAssessedKind.TextOnly);
        e2.NotAssessedReason.ShouldBe("Marginalerna kunde inte läsas ur källfilen.");
    }

    // #2083 E2(a): no margin was read. The margin lives in the layout, not the text (CTO F4 →
    // TextOnly), read as the rubric's pair whole.
    private static void AssertTheRubricTextOnlyPair(CvCriterionVerdict e2)
    {
        var criterion = RealRubric().Criteria.Single(c => c.Id == "E2");
        e2.NotAssessedKind.ShouldBe(NotAssessedKind.TextOnly);
        e2.NotAssessedReason.ShouldBe(criterion.NotAssessedReason);
    }

    // #2083 E2(b): the margin WAS read and is fine; line and section spacing have no measurement
    // yet (CTO F4 → NotInVersion), with E2's own bespoke sentence rather than the rubric's.
    private static void AssertTheMarginsReadPair(CvCriterionVerdict e2)
    {
        e2.NotAssessedKind.ShouldBe(NotAssessedKind.NotInVersion);
        e2.NotAssessedReason.ShouldBe(
            "Marginalerna ser rimliga ut. Radavstånd och luft mellan sektioner bedöms inte i den här versionen.");
    }

    // ===============================================================
    // Profile scoping — E2 is VisualOnly, absent from an ATS review
    // ===============================================================

    [Fact]
    public async Task E2_ShouldNotAppearInAnAtsReview_BecauseItIsVisualOnly()
    {
        var ats = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: CvLayoutMetrics.Analyzed(200_000, 1, 10))),
            RenderProfile.Ats, TestContext.Current.CancellationToken);

        ats.Verdicts.ShouldNotContain(v => v.CriterionId == "E2");
    }
}
