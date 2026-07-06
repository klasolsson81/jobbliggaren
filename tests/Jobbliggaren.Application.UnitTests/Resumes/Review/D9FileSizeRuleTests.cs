using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-6b (#655, ADR 0093 §D4) — D9 "Filstorlek", the imported file size from the
/// ICvLayoutAnalyzer metrics. Driven through the REAL <see cref="CvReviewEngine"/> (all-correct
/// spell-checker stub so C7 never interferes). D9 is an AtsOnly criterion → assessed in the ATS
/// profile. File size is FORMAT-AGNOSTIC: it is known even when geometry is NotApplicable, so
/// D9 assesses a DOCX import too. Semantics:
///   • no metrics (null layout) → NotAssessed;
///   • size &gt; failBytes(5 MB) → Fail; &gt; warnBytes(2 MB) → Warn; else Pass;
///   • the copy uses a Swedish decimal COMMA ("1,5 MB"), never a point.
/// Both bounds are rubric v2.1 DATA (thresholds), derived here from the real asset.
/// </summary>
public class D9FileSizeRuleTests
{
    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist());

    private static async Task<CvCriterionVerdict> D9Async(CvLayoutMetrics? layout)
    {
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: layout)),
            RenderProfile.Ats, TestContext.Current.CancellationToken);
        return Verdict(result, "D9");
    }

    // The soft/hard file-size ceilings as versioned DATA (never literals in the test).
    private static long WarnBytes() =>
        (long)RealRubric().Criteria.Single(c => c.Id == "D9")
            .RequiredThreshold(RubricThresholdKeys.FileSizeWarnBytes);

    private static long FailBytes() =>
        (long)RealRubric().Criteria.Single(c => c.Id == "D9")
            .RequiredThreshold(RubricThresholdKeys.FileSizeFailBytes);

    // ===============================================================
    // Pass / Warn / Fail bands over an Analyzed PDF
    // ===============================================================

    [Fact]
    public async Task D9_ShouldPass_WhenFileSizeIsWellUnderTheWarnCeiling()
    {
        var d9 = await D9Async(CvLayoutMetrics.Analyzed(fileSizeBytes: 800_000, pageCount: 1, minMarginPoints: 40));

        d9.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task D9_ShouldPass_WhenFileSizeIsExactlyTheWarnCeiling()
    {
        // Strict boundary: > warnBytes warns, so exactly-at is still Pass.
        var d9 = await D9Async(CvLayoutMetrics.Analyzed(WarnBytes(), 1, 40));

        d9.Verdict.ShouldBe(CriterionVerdict.Pass, "exakt vid varnings-taket är inte > taket → Pass.");
    }

    [Fact]
    public async Task D9_ShouldWarn_WhenFileSizeIsBetweenTheWarnAndFailCeilings()
    {
        var d9 = await D9Async(CvLayoutMetrics.Analyzed(WarnBytes() + 1, 1, 40));

        d9.Verdict.ShouldBe(CriterionVerdict.Warn);
    }

    [Fact]
    public async Task D9_ShouldFail_WhenFileSizeExceedsTheFailCeiling()
    {
        var d9 = await D9Async(CvLayoutMetrics.Analyzed(FailBytes() + 1, 1, 40));

        d9.Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    // ===============================================================
    // FORMAT-AGNOSTIC — a DOCX (NotApplicable geometry) still assesses on size
    // ===============================================================

    [Fact]
    public async Task D9_ShouldAssessADocxImport_WhenGeometryIsNotApplicableButSizeIsKnown()
    {
        // A DOCX has NotApplicable geometry (no page count / margin) but a REAL file size, so D9
        // must still assess it — here a small DOCX → Pass (never NotAssessed on a known size).
        var d9 = await D9Async(CvLayoutMetrics.NotApplicable(fileSizeBytes: 350_000));

        d9.Verdict.ShouldBe(CriterionVerdict.Pass, "en DOCX bedöms på filstorlek trots NotApplicable-geometri.");
    }

    [Fact]
    public async Task D9_ShouldWarnADocxImport_WhenItsSizeIsOverTheWarnCeiling()
    {
        var d9 = await D9Async(CvLayoutMetrics.NotApplicable(WarnBytes() + 1));

        d9.Verdict.ShouldBe(CriterionVerdict.Warn, "en stor DOCX varnas på storlek (format-agnostiskt).");
    }

    // ===============================================================
    // Swedish decimal-comma copy (CLAUDE.md §10)
    // ===============================================================

    [Fact]
    public async Task D9_ShouldRenderTheSizeWithADecimalComma_WhenItWarns()
    {
        // Exactly 3,0 MB (3 × 1024 × 1024) sits in the Warn band → the size renders as "3,0 MB".
        const long threeMegabytes = 3L * 1024 * 1024;
        var d9 = await D9Async(CvLayoutMetrics.Analyzed(threeMegabytes, 1, 40));
        var observation = d9.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

        d9.Verdict.ShouldBe(CriterionVerdict.Warn);
        observation.ShouldContain("MB");
        // Swedish decimal COMMA in the megabyte value (§10), never a decimal point.
        observation.ShouldContain("3,0");
        observation.ShouldNotContain("3.0");
    }

    // ===============================================================
    // NotAssessed — no metrics at all
    // ===============================================================

    [Fact]
    public async Task D9_ShouldNotAssess_WhenLayoutMetricsAreAbsent()
    {
        var d9 = await D9Async(layout: null);

        d9.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d9.Evidence.ShouldBeEmpty();
        d9.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
    }
}
