using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Rubric v2.0.0 (Fas 4b PR-6a, #655; thresholds-as-data introduced #654 CTO-bind D1/D2c) — the
/// SHIPPED rubric must carry every named threshold key its rules read (fail-loud only fires on a
/// corrupt/N-1 asset, never in production), and the styleOnly set must match the Klas-confirmed proposal.
/// Both are pinned against the REAL committed asset via the real <see cref="IRubricProvider"/>
/// (golden source) so a silent data drift fails CI rather than surfacing at review time.
///
/// This asserts DATA PRESENCE + CLASSIFICATION only; it never changes a threshold VALUE (a value
/// change is a product decision out of PR-5 scope — CTO STOPP screen).
/// </summary>
public class RubricThresholdCompletenessTests
{
    // Every criterion whose rule reads a threshold → the EXACT set of keys it requires. Keys via
    // RubricThresholdKeys constants, never inline strings (§5). Pinned BOTH ways: a missing key
    // would fail the live engine (fail-loud), an extra/renamed key is a silent data drift — both
    // fail here. Mirrors the rule reads: ContentRules (A1/A2/A4/A6/A7/A8/A9), StructureRules
    // (B2/B6), LanguageRules (C2/C3/C6/C7), AtsRules (D9), VisualRules (E2). C7 (Stavning
    // maskinell kontroll) joined in Fas 4b PR-6a (#655); B2 (sidantal) / D9 (filstorlek) /
    // E2 (whitespace) joined in Fas 4b PR-6b — geometry thresholds from ICvLayoutAnalyzer.
    private static readonly Dictionary<string, string[]> RequiredKeysByCriterion =
        new(StringComparer.Ordinal)
        {
            ["A1"] = [RubricThresholdKeys.FailRatio],
            ["A2"] = [RubricThresholdKeys.PassRatio, RubricThresholdKeys.FailRatio],
            ["A4"] = [RubricThresholdKeys.MaxGapMonths],
            ["A6"] = [RubricThresholdKeys.PassRatio, RubricThresholdKeys.FailRatio],
            ["A7"] = [RubricThresholdKeys.PassBelowCount, RubricThresholdKeys.FailFromCount],
            ["A8"] = [RubricThresholdKeys.MaxWords],
            ["A9"] = [RubricThresholdKeys.FailFromCount],
            ["B2"] = [RubricThresholdKeys.MaxPages],
            ["B6"] = [RubricThresholdKeys.MaxDistinctDateFormats],
            ["C2"] = [RubricThresholdKeys.WarnFromExclamationCount],
            ["C3"] = [RubricThresholdKeys.FailRatio],
            ["C6"] = [RubricThresholdKeys.MaxUnexplainedAcronyms],
            ["C7"] = [RubricThresholdKeys.WarnFromMisspellingCount],
            ["D9"] = [RubricThresholdKeys.FileSizeWarnBytes, RubricThresholdKeys.FileSizeFailBytes],
            ["E2"] = [RubricThresholdKeys.MinMarginPointsFloor],
        };

    // The Klas-confirm styleOnly proposal (CTO-bind D2c): exactly the cosmetic set. A silent
    // reclassification — especially one that makes a SUBSTANTIVE rule silenceable — fails CI.
    private static readonly string[] ExpectedStyleOnlyIds = ["B5", "B8", "E3", "E4", "E7", "E8"];

    [Fact]
    public void ShippedRubric_ShouldCarryExactlyTheRequiredThresholdKeys_ForEachRuleThatReadsOne()
    {
        var rubric = RealRubric();

        foreach (var (id, keys) in RequiredKeysByCriterion)
        {
            var criterion = rubric.Criteria.Single(c => c.Id == id);

            criterion.Thresholds.ShouldNotBeNull($"{id} måste bära sina trösklar som DATA (rubric v1.2).");
            criterion.Thresholds!.Keys.ShouldBe(keys, ignoreOrder: true,
                $"{id} ska bära exakt nycklarna [{string.Join(", ", keys)}].");

            // The fail-loud accessor resolves each key to a finite value on the ship (never throws).
            foreach (var key in keys)
            {
                double.IsFinite(criterion.RequiredThreshold(key)).ShouldBeTrue(
                    $"{id}.{key} ska vara ett ändligt värde.");
            }
        }
    }

    [Fact]
    public void ShippedRubric_ShouldCarryThresholdsExactlyOnTheCriteriaThatReadThem()
    {
        // The reverse direction: no OTHER criterion silently gained a thresholds object. Pins that
        // the data surface matches exactly the set of rules that read thresholds.
        var rubric = RealRubric();

        var withThresholds = rubric.Criteria
            .Where(c => c.Thresholds is { Count: > 0 })
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        withThresholds.ShouldBe(RequiredKeysByCriterion.Keys, ignoreOrder: true,
            "endast de regler som läser trösklar får bära ett thresholds-objekt.");
    }

    [Fact]
    public void ShippedRubric_ShouldMarkExactlyTheProposedStyleOnlySet_AndNoneCritical()
    {
        var rubric = RealRubric();

        var styleOnly = rubric.Criteria.Where(c => c.StyleOnly).Select(c => c.Id).ToList();
        styleOnly.ShouldBe(ExpectedStyleOnlyIds, ignoreOrder: true,
            "styleOnly-mängden ska vara exakt det konservativa förslaget {B5,B8,E3,E4,E7,E8} (D2c).");

        // Fail-closed: no style-ignorable criterion may be a critical fail (the loader also guards
        // this, ValidateStyleOnlyNeverCritical — pinned here at the asset level too).
        styleOnly.Intersect(rubric.CriticalFailIds).ShouldBeEmpty(
            "ett styleOnly-kriterium får aldrig vara en kritisk fail (fail-closed).");
    }
}
