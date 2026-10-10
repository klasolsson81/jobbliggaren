using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// The per-criterion projections in <see cref="CvReviewDtoMapper"/>: the static facts of a
/// criterion (its heading, <c>IsIgnorable</c> from the rubric's StyleOnly flag, its
/// "Godkänns när" clause) are derived here from the rubric's criteria, which both review
/// handlers pass from one <c>GetRubric()</c> call.
///
/// The honesty invariants this backs (CLAUDE.md §5): the FE gates the "Ignorera regeln
/// (stilfråga)" control on <c>IsIgnorable</c>, so an over-broad projection would offer a rule
/// the backend then rejects (400 <c>FindingNotIgnorable</c>); and "Godkänns när" on a row the
/// engine did not fail would read as a judgement it never made (#2083, CTO F6).
/// </summary>
public class CvReviewDtoMapperTests
{
    private static readonly RubricVersion Version = RubricVersion.Parse("1.0.0");

    private static IReadOnlyList<RubricCriterion> Criteria => RealRubric().Criteria;

    private static CvCriterionVerdict Assessed(
        string criterionId, CriterionVerdict verdict, RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(
            criterionId, category, verdict,
            [new TextSpanEvidence(new TextSpan(0, 6, "driven"), "note")]);

    private static CvCriterionVerdict Fail(
        string criterionId, RubricCategory category = RubricCategory.Content) =>
        Assessed(criterionId, CriterionVerdict.Fail, category);

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(Version, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    private static string PassCriterionOf(string criterionId) =>
        Criteria.Single(c => c.Id == criterionId).PassCriterion!;

    // ── IsIgnorable: exactly the rubric's StyleOnly set ──

    [Fact]
    public void ToDto_ShouldFlagOnlyStyleOnlyCriteria_AsIgnorable()
    {
        // B5 is StyleOnly in the shipped rubric; A7 is not.
        var result = ResultWith(
            Assessed("B5", CriterionVerdict.Warn, RubricCategory.Structure),
            Fail("A7"));

        var dto = result.ToDto(Criteria);

        dto.Verdicts.Single(v => v.CriterionId == "B5").IsIgnorable.ShouldBeTrue();
        dto.Verdicts.Single(v => v.CriterionId == "A7").IsIgnorable.ShouldBeFalse();
    }

    [Fact]
    public void ToDto_ShouldProjectACriticalFailAsItsVerdictsEntry()
    {
        // CriticalFails is a PARALLEL per-verdict projection; the two lists must never disagree
        // about the same finding. A1 is a critical-fail criterion whose rule fails.
        var a1 = Fail("A1");
        var result = new CvReviewResult(
            Version, RenderProfile.Ats, [], [a1], CriticalFails: [a1], AssessedCount: 1, TotalCount: 1);

        var dto = result.ToDto(Criteria);

        var verdict = dto.Verdicts.Single();
        var critical = dto.CriticalFails.Single();
        critical.Name.ShouldBe(verdict.Name);
        critical.IsIgnorable.ShouldBe(verdict.IsIgnorable);
        critical.PassCriterion.ShouldBe(PassCriterionOf("A1"));
        verdict.PassCriterion.ShouldBe(PassCriterionOf("A1"));
    }

    [Fact]
    public void ToDto_ShouldFallBackToTheIdAndNoRubricFacts_WhenTheRubricLacksTheCriterion()
    {
        // Declared unreachable: the engine and both handlers read the same rubric singleton, so a
        // verdict always has its criterion. This pins only that the read side degrades safely if
        // that invariant breaks: no throw, the id as the name, no rubric fact claimed.
        var result = ResultWith(Fail("A7"));

        var dto = result.ToDto([]);

        var verdict = dto.Verdicts.ShouldHaveSingleItem();
        verdict.Name.ShouldBe("A7");
        verdict.IsIgnorable.ShouldBeFalse();
        verdict.PassCriterion.ShouldBeNull();
    }

    // ── PassCriterion: only where a pass condition was failed (#2083, CTO F6) ──

    [Theory]
    [InlineData(CriterionVerdict.Fail)]
    [InlineData(CriterionVerdict.Warn)]
    public void ToDto_ShouldCarryTheRubricPassCriterion_WhenTheVerdictFailedIt(CriterionVerdict outcome)
    {
        // A2's rule returns both Fail and Warn.
        var result = ResultWith(Assessed("A2", outcome));

        var dto = result.ToDto(Criteria);

        dto.Verdicts.ShouldHaveSingleItem().PassCriterion.ShouldBe(PassCriterionOf("A2"));
    }

    [Fact]
    public void ToDto_ShouldCarryNoPassCriterion_WhenTheVerdictPassed()
    {
        var result = ResultWith(Assessed("A8", CriterionVerdict.Pass));

        var dto = result.ToDto(Criteria);

        dto.Verdicts.ShouldHaveSingleItem().PassCriterion.ShouldBeNull();
    }

    [Fact]
    public void ToDto_ShouldCarryNoPassCriterion_WhenTheVerdictIsNotAssessed()
    {
        // A8 carries a passCriterion in the rubric, so a null here is the mapper's gate, not
        // an absent value.
        var a8 = CvCriterionVerdict.NotAssessed(
            "A8", RubricCategory.Content, NotAssessedKind.InsufficientData,
            Criteria.Single(c => c.Id == "A8").NotAssessedReason!);

        var dto = ResultWith(a8).ToDto(Criteria);

        dto.Verdicts.ShouldHaveSingleItem().PassCriterion.ShouldBeNull();
    }

    [Theory]
    [InlineData("B5", RubricCategory.Structure)]
    [InlineData("E2", RubricCategory.VisualQuality)]
    public void ToDto_ShouldCarryNoPassCriterion_ForAWarnOfARuleThatCanNeverPass(
        string criterionId, RubricCategory category)
    {
        // B5 and E2 return Warn or NotAssessed, never Pass; a pass condition for them would
        // promise an outcome the engine cannot produce, so the rubric authors none.
        var result = ResultWith(Assessed(criterionId, CriterionVerdict.Warn, category));

        var dto = result.ToDto(Criteria);

        dto.Verdicts.ShouldHaveSingleItem().PassCriterion.ShouldBeNull();
    }

    // ── NotAssessedKind: the kind's name on the wire, null on an assessed verdict ──

    [Fact]
    public void ToDto_ShouldProjectTheNotAssessedKindByName_WhenNotAssessed()
    {
        var a3 = CvCriterionVerdict.NotAssessed(
            "A3", RubricCategory.Content, NotAssessedKind.RequiresJobAd,
            Criteria.Single(c => c.Id == "A3").NotAssessedReason!);

        var dto = ResultWith(a3).ToDto(Criteria);

        dto.Verdicts.ShouldHaveSingleItem().NotAssessedKind.ShouldBe("RequiresJobAd");
    }

    [Fact]
    public void ToDto_ShouldCarryNoNotAssessedKind_WhenAssessed()
    {
        var dto = ResultWith(Fail("A7")).ToDto(Criteria);

        dto.Verdicts.ShouldHaveSingleItem().NotAssessedKind.ShouldBeNull();
    }

    // ── #1062 B1: the band crosses the Application boundary as ABSENCE ──
    // This mapper line is the ONE seam where B1's §5 breach can be put back with the whole
    // engine suite still green — `?? "NotReady"`, or the property made non-nullable again, moves
    // the floor label to the client and nothing else turns red.

    [Fact]
    public void ToDto_ShouldKeepAnUnbandedCategoryNullOnTheWire_WhenNoCriterionWasAssessed()
    {
        // The state is the engine's own: BuildCategories emits Band = null for a category whose
        // assessed weight sum is zero, which is what a text-extracted CV produces for
        // VisualQuality under the Visual profile (pinned at the engine altitude in
        // CvReviewEngineTests.ReviewAsync_ShouldReportEveryVisualCriterionAsNotAssessed_…).
        var e3 = CvCriterionVerdict.NotAssessed(
            "E3", RubricCategory.VisualQuality, NotAssessedKind.TextOnly,
            Criteria.Single(c => c.Id == "E3").NotAssessedReason!);
        var unbanded = new CvCategoryResult(
            RubricCategory.VisualQuality,
            PassCount: 0, WarnCount: 0, FailCount: 0, NotAssessedCount: 8,
            Band: null, Verdicts: [e3]);
        var result = new CvReviewResult(
            Version, RenderProfile.Visual, [unbanded], [e3],
            CriticalFails: [], AssessedCount: 0, TotalCount: 8);

        var dto = result.ToDto(Criteria);

        var category = dto.Categories.ShouldHaveSingleItem();
        category.Band.ShouldBeNull(
            "an unbanded state must not acquire a label at the transport boundary.");
        category.NotAssessedCount.ShouldBe(8,
            "the counters still carry the coverage the band refuses to claim anything about.");
    }

    [Fact]
    public void ToDto_ShouldProjectTheBandName_WhenTheCategoryWasAssessed()
    {
        // Counterfactual to the test above: without it, a mapper returning null for EVERY
        // category would pass, and the band would vanish for every client.
        var a7 = Fail("A7");
        var banded = new CvCategoryResult(
            RubricCategory.Content,
            PassCount: 0, WarnCount: 0, FailCount: 1, NotAssessedCount: 0,
            Band: ScoreBandLabel.NeedsRework, Verdicts: [a7]);
        var result = new CvReviewResult(
            Version, RenderProfile.Ats, [banded], [a7],
            CriticalFails: [], AssessedCount: 1, TotalCount: 1);

        var dto = result.ToDto(Criteria);

        dto.Categories.ShouldHaveSingleItem().Band.ShouldBe("NeedsRework");
    }
}
