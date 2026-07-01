using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Taxonomy;

/// <summary>
/// #471 (spun out of #268/#428 finding #6): pins the DETERMINISTIC label tie-break in
/// <see cref="TaxonomyReadModel.BuildLabelByConceptId"/>. When one concept-id carries more
/// than one DIVERGING label, the reverse-lookup must resolve it to the Ordinal-MINIMUM
/// label, never an enumeration-order-dependent First() (parity with
/// <c>OccupationCodeDeriver</c>'s no-silent-First discipline, #373 #5, ADR 0040/0071).
/// <para>
/// The trigger is schema-UNREACHABLE through the DB: <c>taxonomy_concepts.ConceptId</c> is
/// the PRIMARY KEY (TaxonomyConceptConfiguration), so a duplicate concept-id can never be
/// loaded and every group is a singleton; the pin is defensive parity, not a production
/// scenario. This test therefore feeds a hand-rolled, deliberately-impossible
/// duplicate-ConceptId list straight to the internal helper (the only way to exercise the
/// tie-break): no DB / no Testcontainers, a fast parallel-safe [Fact], mirroring
/// <c>OccupationCodeDeriverGroupLabelTiebreakTests</c>.
/// </para>
/// </summary>
public sealed class TaxonomyReadModelLabelTiebreakTests
{
    private const string ConceptId = "duplicate_concept_id";

    // Two labels for the SAME concept-id. Listed so the Ordinal-MAXIMUM label comes first
    // (old First() would wrongly pick it); the fix must pick the Ordinal-MINIMUM label
    // ("Aaa..." sorts before "Zzz..." Ordinal).
    private const string OrdinalMaxLabel = "Zzz (enumeration-order, old First would win)";
    private const string OrdinalMinLabel = "Aaa (Ordinal-minimum, the deterministic pick)";

    private static List<TaxonomyConcept> CraftedConcepts() =>
    [
        new TaxonomyConcept
        {
            ConceptId = ConceptId,
            Kind = TaxonomyConceptKind.OccupationGroup,
            Label = OrdinalMaxLabel,
        },
        new TaxonomyConcept
        {
            ConceptId = ConceptId,
            Kind = TaxonomyConceptKind.OccupationGroup,
            Label = OrdinalMinLabel,
        },
    ];

    [Fact]
    public void BuildLabelByConceptId_DuplicateConceptIdWithDivergingLabels_PicksOrdinalMinimum()
    {
        var result = TaxonomyReadModel.BuildLabelByConceptId(CraftedConcepts());

        result[ConceptId].ShouldBe(
            OrdinalMinLabel,
            "a concept-id with two diverging labels must resolve to the Ordinal-minimum "
            + "label deterministically, never the enumeration-order First (#471/#268)");
    }

    [Fact]
    public void BuildLabelByConceptId_Tiebreak_IsIndependentOfInputOrder()
    {
        var forward = TaxonomyReadModel.BuildLabelByConceptId(CraftedConcepts())[ConceptId];

        var reversed = CraftedConcepts();
        reversed.Reverse();
        var backward = TaxonomyReadModel.BuildLabelByConceptId(reversed)[ConceptId];

        forward.ShouldBe(OrdinalMinLabel);
        backward.ShouldBe(
            OrdinalMinLabel,
            "the tie-break must not depend on the input enumeration order");
    }

    [Fact]
    public void BuildLabelByConceptId_SingletonGroupsPerConceptId_PassThroughUnchanged()
    {
        // The production shape: every concept-id is unique (PK), so every group is a
        // singleton and the tie-break never fires. Pins that introducing OrderBy does not
        // change label passthrough or the key set for the normal (only reachable) path.
        List<TaxonomyConcept> concepts =
        [
            new TaxonomyConcept
            {
                ConceptId = "concept_a",
                Kind = TaxonomyConceptKind.OccupationGroup,
                Label = "Alfa",
            },
            new TaxonomyConcept
            {
                ConceptId = "concept_b",
                Kind = TaxonomyConceptKind.OccupationGroup,
                Label = "Beta",
            },
        ];

        var result = TaxonomyReadModel.BuildLabelByConceptId(concepts);

        result.Count.ShouldBe(2);
        result["concept_a"].ShouldBe("Alfa");
        result["concept_b"].ShouldBe("Beta");
    }
}
