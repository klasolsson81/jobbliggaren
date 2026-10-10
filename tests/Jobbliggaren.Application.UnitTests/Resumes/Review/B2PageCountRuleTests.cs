using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-6b (#655, ADR 0093 §D4) — B2 "Längd", the imported PDF's page count from the
/// ICvLayoutAnalyzer geometry. Driven through the REAL <see cref="CvReviewEngine"/> (all-correct
/// spell-checker stub so C7 never interferes) against a <see cref="CvReviewContext.FromParsed"/>
/// of a <see cref="ParsedResume"/> carrying a chosen <see cref="CvLayoutMetrics"/>. B2 is a Both
/// criterion, so it is assessed in the ATS profile. Semantics:
///   • no geometry (null layout, or a Failed parse) → NotAssessed with an honest reason;
///   • pages &gt; maxPages(2) → Warn (cites the page count) — WARN, never Fail (an academic CV
///     can legitimately run longer; the page count alone cannot detect the exception);
///   • pages ≤ 2 → Pass.
/// The recommended ceiling is rubric v2.1 DATA (thresholds.maxPages), so the boundary is derived
/// from the real asset, never a hardcoded literal here.
/// </summary>
public class B2PageCountRuleTests
{
    private static CvReviewEngine Engine(IRubricProvider? rubric = null) =>
        new(rubric ?? RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> B2Async(CvLayoutMetrics? layout, IRubricProvider? rubric = null)
    {
        var result = await Engine(rubric).ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: layout)),
            RenderProfile.Ats, TestContext.Current.CancellationToken);
        return Verdict(result, "B2");
    }

    // The recommended page ceiling as versioned DATA (never a literal in the test).
    private static int MaxPages() =>
        (int)RealRubric().Criteria.Single(c => c.Id == "B2")
            .RequiredThreshold(RubricThresholdKeys.MaxPages);

    // ===============================================================
    // Pass — at or under the recommended ceiling
    // ===============================================================

    [Fact]
    public async Task B2_ShouldPass_WhenPageCountIsOne()
    {
        var b2 = await B2Async(CvLayoutMetrics.Analyzed(fileSizeBytes: 120_000, pageCount: 1, minMarginPoints: 40));

        b2.Verdict.ShouldBe(CriterionVerdict.Pass);
        b2.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldContain("1");
    }

    [Fact]
    public async Task B2_ShouldPass_WhenPageCountIsExactlyTheCeiling()
    {
        var b2 = await B2Async(CvLayoutMetrics.Analyzed(120_000, MaxPages(), 40));

        b2.Verdict.ShouldBe(CriterionVerdict.Pass, "vid taket (≤ maxPages) → Pass (strikt gräns).");
    }

    // ===============================================================
    // Warn — over the ceiling; cites the page count; never Fail
    // ===============================================================

    [Fact]
    public async Task B2_ShouldWarnAndCiteThePageCount_WhenPageCountExceedsTheCeiling()
    {
        var pages = MaxPages() + 1;
        var b2 = await B2Async(CvLayoutMetrics.Analyzed(400_000, pages, 40));

        b2.Verdict.ShouldBe(CriterionVerdict.Warn);
        b2.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldContain(pages.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task B2_ShouldNeverFail_EvenForAVeryLongCv()
    {
        // Academic-exception honesty: an over-long CV nudges (Warn), it is never a Fail.
        var b2 = await B2Async(CvLayoutMetrics.Analyzed(900_000, pageCount: 12, minMarginPoints: 40));

        b2.Verdict.ShouldBe(CriterionVerdict.Warn);
        b2.Verdict.ShouldNotBe(CriterionVerdict.Fail, "B2 flaggar aldrig Fail på sidantal (akademiskt undantag).");
    }

    // ===============================================================
    // NotAssessed — no geometry (null layout, or a Failed PDF parse)
    // ===============================================================

    [Fact]
    public async Task B2_ShouldNotAssess_WhenLayoutMetricsAreAbsent()
    {
        var b2 = await B2Async(layout: null);

        b2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        b2.Evidence.ShouldBeEmpty();
        b2.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
        AssertTheRubricTextOnlyPair(b2);
    }

    [Fact]
    public async Task B2_ShouldNotAssess_WhenTheGeometryParseFailed()
    {
        // A Failed analysis has a known file size but null PageCount → B2 verdicts NotAssessed
        // (honest ceiling), never a fabricated Pass on absent geometry.
        var b2 = await B2Async(CvLayoutMetrics.Failed(fileSizeBytes: 8));

        b2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        b2.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
        AssertTheRubricTextOnlyPair(b2);
    }

    [Fact]
    public async Task B2_ShouldUseItsOwnFallbackPair_WhenTheAssetPredatesTheKind()
    {
        // The shipped asset as it stood before #2083 (reason, no kind): the rubric reason is never
        // shown beside a guessed kind, so B2's own code pair is used whole (CTO F2).
        var b2 = await B2Async(layout: null, RubricPredatingNotAssessedKind());

        b2.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        b2.NotAssessedKind.ShouldBe(NotAssessedKind.TextOnly);
        b2.NotAssessedReason.ShouldBe("CV-längden kunde inte läsas ur källfilen.");
    }

    // #2083: the page count lives in the file, not the text (CTO F4 → TextOnly), read as the
    // rubric's pair whole.
    private static void AssertTheRubricTextOnlyPair(CvCriterionVerdict b2)
    {
        var criterion = RealRubric().Criteria.Single(c => c.Id == "B2");
        b2.NotAssessedKind.ShouldBe(NotAssessedKind.TextOnly);
        b2.NotAssessedReason.ShouldBe(criterion.NotAssessedReason);
    }
}
