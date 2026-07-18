using System.Text.RegularExpressions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — the committed CV-rubric (rubric.v1.2.0.json) loads through
/// the real <see cref="RubricProvider"/> and satisfies the architect's authoritative
/// classification (ADR 0071 OQ3 / ADR 0074). The rubric is VERSIONED DATA, not C#
/// literals — these tests prove the data file (not a hardcoded list) is the source of
/// the thresholds, criteria, and category weights.
///
/// Drift-robustness (mirrors TaxonomySnapshotSeederTests): per-category counts assert
/// `>=` floors + structural invariants, NOT brittle exact totals, since authoring may
/// legitimately add criteria. The TWO load-bearing facts are pinned HARD:
///   - A5 and C1 are NotAssessedV1 (ADR 0071 OQ3 — the reduced-precision criteria
///     that must be marked "not assessed v1", never mis-reported), and
///   - exactly 2 criteria are NotAssessedV1.
///
/// RED until RubricProvider ships internal sealed in Jobbliggaren.Infrastructure.KnowledgeBank
/// and the contract types ship in Jobbliggaren.Application.KnowledgeBank.Abstractions.
/// </summary>
public class RubricProviderTests
{
    private static Rubric LoadRubric() => new RubricProvider().GetRubric();

    [Fact]
    public void GetRubric_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var rubric = LoadRubric();

        rubric.ShouldNotBeNull();
        // The committed rubric is the v1 baseline (ADR 0074). Version is carried as
        // DATA (RubricVersion), not a C# literal. Bumped 2.1.0 → 2.2.0 (#891: D3 got a reader +
        // fontBodyPtWarnBelow threshold → MINOR bump, asset renamed rubric.v2.2.0.json). Prior:
        // 2.0.0 → 2.1.0 (#655 PR-6b: B2/D9/E2 gained geometry thresholds → MINOR bump per the
        // RubricVersion doctrine — thresholds added, no new scored criterion); 1.2.0 → 2.0.0
        // (#655 PR-6a: the C7 spelling criterion ADDED → MAJOR bump); 1.1.0 → 1.2.0 (#654,
        // thresholds-as-data + styleOnly); 1.0.1 → 1.1.0 (#488).
        rubric.Version.ShouldBe(RubricVersion.Parse("2.2.0"));
        rubric.EffectiveDate.ShouldBeGreaterThan(default(DateOnly));
    }

    // ───────────────────────────────────────────────────────────────────
    // Category counts — drift-robust floors (architect: A>=10, B>=8, C>=6,
    // D>=10, E>=8, total >=42). Assert `>=`, not exact, so authoring can grow.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRubric_ShouldHaveAllFiveCategoriesAboveTheirFloors_WhenCalled()
    {
        var rubric = LoadRubric();

        rubric.Criteria.Count.ShouldBeGreaterThanOrEqualTo(42);
        CountIn(rubric, RubricCategory.Content).ShouldBeGreaterThanOrEqualTo(10);
        CountIn(rubric, RubricCategory.Structure).ShouldBeGreaterThanOrEqualTo(8);
        CountIn(rubric, RubricCategory.Language).ShouldBeGreaterThanOrEqualTo(6);
        CountIn(rubric, RubricCategory.AtsParsability).ShouldBeGreaterThanOrEqualTo(10);
        CountIn(rubric, RubricCategory.VisualQuality).ShouldBeGreaterThanOrEqualTo(8);

        // Every category is represented (no empty category slipped through).
        foreach (var category in Enum.GetValues<RubricCategory>())
        {
            CountIn(rubric, category).ShouldBeGreaterThan(0,
                $"Kategori {category} saknar kriterier.");
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Assessability (ADR 0071 OQ3) — the load-bearing honesty contract.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRubric_ShouldMarkA5C1AndC5AsNotAssessedV1_WhenCalled()
    {
        // HARD pin (load-bearing, ADR 0071 OQ3): A5, C1 and C5 are the reduced-precision
        // criteria — they MUST be marked NotAssessedV1 and never mis-reported as
        // deterministically assessed. This is the central honesty invariant. C5 joined
        // the set in #488 (sentence-level sv/en mixing is not assessed v1, parity A5/C1).
        var rubric = LoadRubric();

        Criterion(rubric, "A5").Assessability.ShouldBe(CriterionAssessability.NotAssessedV1);
        Criterion(rubric, "C1").Assessability.ShouldBe(CriterionAssessability.NotAssessedV1);
        Criterion(rubric, "C5").Assessability.ShouldBe(CriterionAssessability.NotAssessedV1);
    }

    [Fact]
    public void GetRubric_ShouldHaveExactlyThreeNotAssessedV1Criteria_WhenCalled()
    {
        // Exactly three criteria are NotAssessedV1 (A5 + C1 + C5). Pinned by COUNT (==3);
        // combined with the by-id pin above this proves the set is precisely {A5, C1, C5}.
        var rubric = LoadRubric();

        rubric.Criteria
            .Count(c => c.Assessability == CriterionAssessability.NotAssessedV1)
            .ShouldBe(3);
    }

    [Fact]
    public void GetRubric_ShouldMarkTheBorderlineCriteriaAsDeterministicPlusNlp_WhenCalled()
    {
        // The det+nlp criteria per the architect's classification, minus C5 which was
        // reclassified NotAssessedV1 in #488 (A2,A3,A6,A7,A9,C2,C3,C4,D8). Asserted by
        // THESE SPECIFIC ids being det+nlp (drift-robust: NOT pinned as an exact global
        // det+nlp count, since authoring may legitimately move a borderline criterion —
        // the load-bearing pin is A5/C1/C5=NotAssessedV1 above, not this set's cardinality).
        var rubric = LoadRubric();

        string[] detPlusNlp = ["A2", "A3", "A6", "A7", "A9", "C2", "C3", "C4", "D8"];
        foreach (var id in detPlusNlp)
        {
            Criterion(rubric, id).Assessability
                .ShouldBe(CriterionAssessability.DeterministicPlusNlp,
                    $"Kriterium {id} ska vara DeterministicPlusNlp enligt arkitektens klassificering.");
        }
    }

    [Fact]
    public void GetRubric_ShouldGiveEveryCriterionANonDefaultAssessability_WhenCalled()
    {
        // No criterion may silently fall back to the enum default — every criterion's
        // assessability must be explicitly one of the three known values. (Drift-robust
        // alternative to a brittle global deterministic-count assertion.)
        var rubric = LoadRubric();

        var known = Enum.GetValues<CriterionAssessability>().ToHashSet();
        rubric.Criteria.ShouldAllBe(c => known.Contains(c.Assessability));
        // The remainder (neither NotAssessedV1 nor DeterministicPlusNlp) are plain
        // Deterministic — the majority of the rubric.
        rubric.Criteria
            .Count(c => c.Assessability == CriterionAssessability.Deterministic)
            .ShouldBeGreaterThan(0);
    }

    // ───────────────────────────────────────────────────────────────────
    // NotAssessedReason as DATA (reason-relocation STEG, ADR 0071: the
    // "ej bedömt" copy is versioned rubric DATA, not an inline C# switch in
    // CvReviewEngine; CLAUDE.md §10/§5: civic Swedish, never dev-jargon).
    //
    // RED until RubricCriterion gains `string? NotAssessedReason` (trailing
    // optional param) and the asset (rubric.v1.2.0.json) authors the field.
    // ───────────────────────────────────────────────────────────────────

    // Test A — the loader maps the authored notAssessedReason JSON field through to
    // the RubricCriterion contract (exact civic Swedish value from the asset).
    [Fact]
    public void GetRubric_ShouldMapNotAssessedReasonFromAsset_ForA5()
    {
        // A5 (Karriärprogression) is pinned NotAssessedV1. Its asset-authored
        // notAssessedReason is the civic Swedish copy below — proving the reason now
        // lives as DATA on the criterion (not in the engine's old PinnedReason switch).
        var rubric = LoadRubric();

        Criterion(rubric, "A5").NotAssessedReason
            .ShouldBe("Vi bedömer inte karriärutveckling i den här versionen.");
    }

    [Fact]
    public void GetRubric_ShouldMapNotAssessedReasonFromAsset_ForC1()
    {
        // C1 (Stavning/grammatik) is pinned NotAssessedV1 with its own civic reason.
        var rubric = LoadRubric();

        Criterion(rubric, "C1").NotAssessedReason
            .ShouldBe("Djupare stavnings- och grammatikkontroll ingår inte i den här versionen.");
    }

    [Fact]
    public void GetRubric_ShouldMapNotAssessedReasonFromAsset_ForC5()
    {
        // C5 (Språkkonsistens sv/en) became pinned NotAssessedV1 in #488. Pin its asset-authored
        // civic reason EXACTLY (parity A5/C1) — this guards the honesty copy #488 introduced
        // against a future edit that swaps it for a misleading-but-civil claim that would still
        // pass the non-null + de-jargon guards (e.g. re-introducing "CV:t är skrivet på ett
        // konsekvent språk", the fabricated assertion #488 removed).
        var rubric = LoadRubric();

        Criterion(rubric, "C5").NotAssessedReason
            .ShouldBe("Vi kontrollerar inte språkblandning mening för mening i den här versionen.");
    }

    // Test G (part 1) — the §10 DE-JARGON GUARD: no criterion's NotAssessedReason may
    // leak developer jargon to a job-seeker. This is the regression-prevention test that
    // keeps "ADR 0071 OQ3", "POS/NER", "F4-5/6", "v1" etc. out of the user surface forever.
    [Fact]
    public void GetRubric_ShouldNeverLeakDevJargonInAnyNotAssessedReason_WhenCalled()
    {
        var rubric = LoadRubric();

        // Case-insensitive forbidden tokens (CLAUDE.md §10 civic tone, §5 no dev-jargon
        // on the user surface). " v1"/"v1 " catch the old "ej bedömt v1" wording without
        // tripping legitimate prose; the rest catch internal codenames/identifiers.
        string[] forbidden =
        [
            "ADR", "POS", "NER", "OQ", "F4-", "F4-5", "F4-6",
            " v1", "v1 ", "matchnings-motor", "parse", "POS/NER",
        ];

        var withReason = rubric.Criteria
            .Where(c => c.NotAssessedReason is not null)
            .ToList();

        withReason.ShouldNotBeEmpty(
            "Minst de NotAssessed-kriterierna ska bära ett civilt skäl i assetet.");

        foreach (var criterion in withReason)
        {
            foreach (var token in forbidden)
            {
                criterion.NotAssessedReason!.Contains(token, StringComparison.OrdinalIgnoreCase)
                    .ShouldBeFalse(
                        $"Kriterium {criterion.Id} läcker dev-jargon '{token}' i NotAssessedReason: " +
                        $"\"{criterion.NotAssessedReason}\". Skälet är användarvänd svensk copy (§10).");
            }
        }
    }

    // Test G (part 2) — every criterion that the engine reports as NotAssessed (pinned
    // NotAssessedV1 OR no registered engine rule) MUST carry a non-null NotAssessedReason
    // in the REAL asset, so production never silently falls back to the code default.
    [Fact]
    public void GetRubric_ShouldGiveEveryEngineNotAssessedCriterionANonNullReason_WhenCalled()
    {
        var rubric = LoadRubric();

        // The criterion ids the engine ALWAYS assesses (produces Pass/Warn/Fail, never
        // NotAssessed) via a registered ICriterionRule. Anything NOT in this set, plus the
        // pinned NotAssessedV1 criteria, resolves to NotAssessed at review time and therefore
        // needs an authored civic reason in the asset. C5 left this set in #488 (its rule was
        // removed) → it now needs an authored notAssessedReason, which this test guards.
        // Fas 4b PR-6a (#655): C7 (spelling) gained a rule that NEVER returns NotAssessed
        // (Pass/Warn only) → added here so it is not required to carry a reason. B5 also gained
        // a rule (B5ConsistentFormattingRule) but its rule CONDITIONALLY returns NotAssessed
        // (single/no marker) reading criterion.NotAssessedReason — so B5 is deliberately KEPT
        // OUT of this set, staying required to carry its authored reason (which it does).
        // #844: A8 LEFT this set. Its rule now CONDITIONALLY returns NotAssessed (when the CV carries
        // unclassified text above its first heading, the absence of a profile is not observed and the
        // claim cannot be grounded) — which makes A8 exactly B5. Keeping it in the exemption would ship
        // a guard whose sentinel is narrower than the set it guards, and that is not a guard, it is a
        // claim: the check would stay green while A8 fell back on a code default instead of the
        // asset's authored civic copy.
        string[] ruleCriteria =
        [
            "A1", "A2", "A4", "A6", "A7", "A9", "A10",
            "B1", "B3", "B4", "B6", "B7", "B8",
            "C2", "C3", "C4", "C6", "C7",
            "D1", "D6",
        ];
        var ruled = ruleCriteria.ToHashSet(StringComparer.Ordinal);

        var notAssessedCriteria = rubric.Criteria
            .Where(c =>
                c.Assessability == CriterionAssessability.NotAssessedV1
                || !ruled.Contains(c.Id))
            .ToList();

        notAssessedCriteria.ShouldNotBeEmpty();
        foreach (var criterion in notAssessedCriteria)
        {
            criterion.NotAssessedReason.ShouldNotBeNullOrWhiteSpace(
                $"Kriterium {criterion.Id} rapporteras NotAssessed av motorn och MÅSTE bära " +
                "ett authored civilt skäl i assetet (annars faller produktionen tillbaka på " +
                "kod-defaulten).");
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Profile rule — D = AtsOnly, E = VisualOnly, A/B/C = Both, with the
    // matching signal-nullability contract.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRubric_ShouldMarkAtsCategoryAsAtsOnlyWithNullVisualSignals_WhenCalled()
    {
        // D1–D10 (AtsParsability) are AtsOnly: ATS signals non-null, Visual signals null.
        var rubric = LoadRubric();

        var ats = rubric.Criteria
            .Where(c => c.Category == RubricCategory.AtsParsability)
            .ToList();
        ats.ShouldNotBeEmpty();
        ats.ShouldAllBe(c => c.Profile == RubricProfile.AtsOnly);
        ats.ShouldAllBe(c => c.AtsPassSignal != null && c.AtsFailSignal != null);
        ats.ShouldAllBe(c => c.VisualPassSignal == null && c.VisualFailSignal == null);
    }

    [Fact]
    public void GetRubric_ShouldMarkVisualCategoryAsVisualOnlyWithNullAtsSignals_WhenCalled()
    {
        // E1–E8 (VisualQuality) are VisualOnly: Visual signals non-null, ATS signals null.
        var rubric = LoadRubric();

        var visual = rubric.Criteria
            .Where(c => c.Category == RubricCategory.VisualQuality)
            .ToList();
        visual.ShouldNotBeEmpty();
        visual.ShouldAllBe(c => c.Profile == RubricProfile.VisualOnly);
        visual.ShouldAllBe(c => c.VisualPassSignal != null && c.VisualFailSignal != null);
        visual.ShouldAllBe(c => c.AtsPassSignal == null && c.AtsFailSignal == null);
    }

    [Fact]
    public void GetRubric_ShouldMarkSharedCategoriesAsBothWithAllFourSignals_WhenCalled()
    {
        // A/B/C (Content/Structure/Language) are Both: all four signals non-null.
        var rubric = LoadRubric();

        RubricCategory[] shared =
            [RubricCategory.Content, RubricCategory.Structure, RubricCategory.Language];
        var both = rubric.Criteria
            .Where(c => shared.Contains(c.Category))
            .ToList();
        both.ShouldNotBeEmpty();
        both.ShouldAllBe(c => c.Profile == RubricProfile.Both);
        both.ShouldAllBe(c =>
            c.AtsPassSignal != null && c.AtsFailSignal != null
            && c.VisualPassSignal != null && c.VisualFailSignal != null);
    }

    // ───────────────────────────────────────────────────────────────────
    // Thresholds-as-data: Weights, CategoryWeights, Bands, CriticalFailIds.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRubric_ShouldCarryTheFourWeightTiers_WhenCalled()
    {
        var rubric = LoadRubric();

        rubric.Weights.Count.ShouldBe(4);
        rubric.Weights[CriterionWeight.Critical].ShouldBe(3d);
        rubric.Weights[CriterionWeight.High].ShouldBe(2d);
        rubric.Weights[CriterionWeight.Medium].ShouldBe(1d);
        rubric.Weights[CriterionWeight.Low].ShouldBe(0.5d);
    }

    [Fact]
    public void GetRubric_ShouldCarryCategoryWeightsForBothProfiles_WhenCalled()
    {
        // ATS profile weights cover A/B/C/D; Visual profile weights cover A/B/C/E.
        var rubric = LoadRubric();

        rubric.CategoryWeights.Ats.Keys.ShouldBe(
            [RubricCategory.Content, RubricCategory.Structure,
             RubricCategory.Language, RubricCategory.AtsParsability],
            ignoreOrder: true);
        rubric.CategoryWeights.Visual.Keys.ShouldBe(
            [RubricCategory.Content, RubricCategory.Structure,
             RubricCategory.Language, RubricCategory.VisualQuality],
            ignoreOrder: true);

        // Weights are real, positive numbers carried as data (not zero/default).
        rubric.CategoryWeights.Ats.Values.ShouldAllBe(w => w > 0);
        rubric.CategoryWeights.Visual.Values.ShouldAllBe(w => w > 0);
    }

    [Fact]
    public void GetRubric_ShouldCarryFourScoreBandsOrderedAscending_WhenCalled()
    {
        // Bands carried as data, ascending by MinInclusive: 0, 50, 70, 85, mapped to
        // NotReady → NeedsRework → Competitive → TopTier.
        var rubric = LoadRubric();

        rubric.Bands.Count.ShouldBe(4);

        var ordered = rubric.Bands.OrderBy(b => b.MinInclusive).ToList();
        // Already ascending in the data (relation invariant — drift-robust).
        rubric.Bands.Select(b => b.MinInclusive).ShouldBe(
            ordered.Select(b => b.MinInclusive));

        ordered.Select(b => b.MinInclusive).ShouldBe([0, 50, 70, 85]);
        ordered.Select(b => b.Label).ShouldBe(
            [ScoreBandLabel.NotReady, ScoreBandLabel.NeedsRework,
             ScoreBandLabel.Competitive, ScoreBandLabel.TopTier]);
    }

    [Fact]
    public void GetRubric_ShouldCarryCriticalFailIdsThatAreSubsetOfCriterionIds_WhenCalled()
    {
        // CriticalFailIds non-empty and ⊆ the criterion ids (research baseline:
        // A1,B4,C1,D1). Subset is the hard invariant (a critical-fail id pointing at a
        // non-existent criterion would be a data bug). Drift-robust: assert subset +
        // the known baseline ids are present, NOT an exact closed set.
        var rubric = LoadRubric();

        rubric.CriticalFailIds.ShouldNotBeEmpty();

        var ids = rubric.Criteria.Select(c => c.Id).ToHashSet();
        rubric.CriticalFailIds.ShouldAllBe(id => ids.Contains(id));

        foreach (var expected in new[] { "A1", "B4", "C1", "D1" })
        {
            rubric.CriticalFailIds.ShouldContain(expected,
                $"Critical-fail-baseline {expected} (research) ska finnas i CriticalFailIds.");
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Criterion id hygiene.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRubric_ShouldHaveUniqueCriterionIds_WhenCalled()
    {
        var rubric = LoadRubric();

        var ids = rubric.Criteria.Select(c => c.Id).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void GetRubric_ShouldHaveCriterionIdsMatchingTheCategoryLetterPattern_WhenCalled()
    {
        // Id pattern ^[A-E](10|[1-9])$ — letter A–E + 1..10, no leading zeros.
        var rubric = LoadRubric();
        var pattern = new Regex("^[A-E](10|[1-9])$");

        rubric.Criteria.ShouldAllBe(c => pattern.IsMatch(c.Id));
    }

    [Fact]
    public void GetRubric_ShouldHaveNonEmptyNameOnEveryCriterion_WhenCalled()
    {
        var rubric = LoadRubric();

        rubric.Criteria.ShouldAllBe(c => !string.IsNullOrWhiteSpace(c.Name));
    }

    private static int CountIn(Rubric rubric, RubricCategory category) =>
        rubric.Criteria.Count(c => c.Category == category);

    private static RubricCriterion Criterion(Rubric rubric, string id) =>
        rubric.Criteria.Single(c => c.Id == id);
}
