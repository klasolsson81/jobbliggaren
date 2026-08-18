using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-8.4 (CTO-bind Q1 = Variant A) — the <c>IsIgnorable</c> projection in
/// <see cref="CvReviewDtoMapper"/>. The mapper is the single place that stamps the
/// per-verdict style-only flag onto the transport DTO from the <c>ignorableCriterionIds</c>
/// set both review handlers derive from the SAME <c>GetRubric()</c> call. These tests pin
/// the projection in isolation (no DbContext, no engine): a verdict whose criterionId is in
/// the set is ignorable, every other verdict is not, and the DEFAULT (no set / empty set)
/// leaves every verdict non-ignorable — the back-compat guarantee for the existing positional
/// callers/tests that never pass the set.
///
/// The honesty invariant this backs (CLAUDE.md §5): the FE gates the "Ignorera regeln
/// (stilfråga)" control on this flag, so an over-broad projection would offer a rule the
/// backend then rejects (400 <c>FindingNotIgnorable</c>). The flag must be exactly the
/// rubric's StyleOnly set — never wider.
/// </summary>
public class CvReviewDtoMapperTests
{
    private static readonly RubricVersion Version = RubricVersion.Parse("1.0.0");

    private static CvCriterionVerdict Fail(
        string criterionId, RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(
            criterionId, category, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, 6, "driven"), "note")]);

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(Version, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    private static Dictionary<string, string> Names(params string[] ids) =>
        ids.ToDictionary(id => id, id => $"Namn {id}", StringComparer.Ordinal);

    [Fact]
    public void ToDto_ShouldFlagOnlyIgnorableCriteria_WhenSetProvided()
    {
        // E3 is in the ignorable (StyleOnly) set; A7 is not.
        var result = ResultWith(
            Fail("E3", RubricCategory.VisualQuality),
            Fail("A7"));
        var ignorable = new HashSet<string>(StringComparer.Ordinal) { "E3" };

        var dto = result.ToDto(Names("E3", "A7"), ignorableCriterionIds: ignorable);

        dto.Verdicts.Single(v => v.CriterionId == "E3").IsIgnorable.ShouldBeTrue();
        dto.Verdicts.Single(v => v.CriterionId == "A7").IsIgnorable.ShouldBeFalse();
    }

    [Fact]
    public void ToDto_ShouldFlagCriticalFailsConsistently_WhenIgnorableIdInCriticalList()
    {
        // CriticalFails is a PARALLEL per-verdict projection — an ignorable id surfaced there
        // must carry the flag identically, or the two lists would disagree about the same finding.
        var e3 = Fail("E3", RubricCategory.VisualQuality);
        var result = new CvReviewResult(
            Version, RenderProfile.Ats, [], [e3], CriticalFails: [e3], AssessedCount: 1, TotalCount: 1);
        var ignorable = new HashSet<string>(StringComparer.Ordinal) { "E3" };

        var dto = result.ToDto(Names("E3"), ignorableCriterionIds: ignorable);

        dto.Verdicts.Single().IsIgnorable.ShouldBeTrue();
        dto.CriticalFails.Single().IsIgnorable.ShouldBeTrue();
    }

    [Fact]
    public void ToDto_ShouldDefaultIsIgnorableToFalseForEveryVerdict_WhenNoSetPassed()
    {
        // Back-compat: existing positional callers pass no ignorable set. Every verdict must
        // then be non-ignorable — the FE hides the Ignorera control (fail-closed).
        var result = ResultWith(
            Fail("E3", RubricCategory.VisualQuality),
            Fail("A7"),
            Fail("B5", RubricCategory.Structure));

        var dto = result.ToDto(Names("E3", "A7", "B5"));

        dto.Verdicts.ShouldAllBe(v => v.IsIgnorable == false);
    }

    [Fact]
    public void ToDto_ShouldDefaultIsIgnorableToFalse_WhenEmptySetPassed()
    {
        var result = ResultWith(Fail("E3", RubricCategory.VisualQuality));

        var dto = result.ToDto(
            Names("E3"),
            ignorableCriterionIds: new HashSet<string>(StringComparer.Ordinal));

        dto.Verdicts.Single().IsIgnorable.ShouldBeFalse();
    }

    // ── #1062 B1: the band crosses the Application boundary as ABSENCE ──
    // Every other test in this file passes Categories: [], so the category projection had no
    // coverage at all. That matters here specifically: this mapper line is the ONE seam where
    // B1's §5 breach can be put back with the whole engine suite still green — `?? "NotReady"`,
    // or the property made non-nullable again, moves the floor label to the client and nothing
    // else turns red.

    [Fact]
    public void ToDto_ShouldKeepAnUnbandedCategoryNullOnTheWire_WhenNoCriterionWasAssessed()
    {
        // The state is the engine's own: BuildCategories emits Band = null for a category whose
        // assessed weight sum is zero, which is what a text-extracted CV produces for
        // VisualQuality under the Visual profile (pinned at the engine altitude in
        // CvReviewEngineTests.ReviewAsync_ShouldReportEveryVisualCriterionAsNotAssessed_…).
        var e3 = CvCriterionVerdict.NotAssessed(
            "E3", RubricCategory.VisualQuality, "Bedöms inte i en textbaserad tolkning.");
        var unbanded = new CvCategoryResult(
            RubricCategory.VisualQuality,
            PassCount: 0, WarnCount: 0, FailCount: 0, NotAssessedCount: 8,
            Band: null, Verdicts: [e3]);
        var result = new CvReviewResult(
            Version, RenderProfile.Visual, [unbanded], [e3],
            CriticalFails: [], AssessedCount: 0, TotalCount: 8);

        var dto = result.ToDto(Names("E3"));

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

        var dto = result.ToDto(Names("A7"));

        dto.Categories.ShouldHaveSingleItem().Band.ShouldBe("NeedsRework");
    }
}
