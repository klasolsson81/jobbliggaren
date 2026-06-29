using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.OccupationDerivation;

/// <summary>
/// #373 nice-to-have #5 (found by the #268 audit) — pins the DETERMINISTIC group-label
/// tie-break in <see cref="OccupationCodeDeriver"/>'s BuildAsync. When one ssyk-4
/// occupation-group concept-id surfaces under more than one occupation field with
/// DIVERGING labels, the deriver must resolve it to the Ordinal-MINIMUM label, never an
/// enumeration-order-dependent First() — this deriver's whole contract is reproducibility
/// (class docstring; ADR 0040/0071).
/// <para>
/// Unlike <c>OccupationCodeDeriverIntegrationTests</c> (real seeded snapshot, which today
/// carries one label per group so the tie never fires), this test feeds a CRAFTED
/// <see cref="ITaxonomyReadModel"/> tree that deliberately lists group q8wL_kdi_WaW under
/// two fields with two labels in NON-Ordinal order — the only way to exercise the
/// defensive tie-break. The crafted occupation carries the REAL occupation-name concept-id
/// tQFo_jhD_UXT ("Advokat"), which the FROZEN <c>OccupationGroupMappingLoader</c> map
/// (occupation-name-to-ssyk-level-4.v30.json, not fakeable) resolves to that group. The
/// real Swedish analyzer is used (parity the integration test). No DB / no Testcontainers —
/// a fast, parallel-safe [Fact].
/// </para>
/// </summary>
public sealed class OccupationCodeDeriverGroupLabelTiebreakTests
{
    // Real frozen-map pair — provenance occupation-name-to-ssyk-level-4.v30.json:
    // "tQFo_jhD_UXT": "q8wL_kdi_WaW" (the real snapshot group label is "Advokater").
    private const string OccupationConceptId = "tQFo_jhD_UXT";
    private const string OccupationName = "Advokat";
    private const string GroupConceptId = "q8wL_kdi_WaW";

    // Two labels for the SAME group id. Ordered so SelectMany yields the Ordinal-MAXIMUM
    // ("Z…") first — old First() would wrongly pick it; the fix must pick the Ordinal-
    // MINIMUM ("A…", since 'A' < 'Z' Ordinal).
    private const string OrdinalMaxLabel = "Zzz Advokater (enumeration-order — old First would win)";
    private const string OrdinalMinLabel = "Aaa Advokater (Ordinal-minimum — the deterministic pick)";

    private static OccupationCodeDeriver NewDeriver()
    {
        var taxonomy = new FakeTaxonomy(CraftedTree());
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        return new OccupationCodeDeriver(taxonomy, analyzer);
    }

    // Hand-rolled fake (not NSubstitute): GetTreeAsync returns a FRESH ValueTask each call
    // — a mock replaying one cached ValueTask trips CA2012 (single-consumption). The
    // deriver only ever reads GetTreeAsync; the other three port members are unreachable.
    private sealed class FakeTaxonomy(TaxonomyTreeDto tree) : ITaxonomyReadModel
    {
        public ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken) => new(tree);

        public ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
            IReadOnlyList<string> conceptIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationCodeDeriver only reads GetTreeAsync.");

        public ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
            string prefix, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationCodeDeriver only reads GetTreeAsync.");

        public ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
            IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationCodeDeriver only reads GetTreeAsync.");
    }

    // Field A lists the group with the Ordinal-MAX label first + the occupation; Field B
    // lists the SAME group id with the Ordinal-MIN label. SelectMany over [A, B] yields
    // [Max, Min] → old First() = Max (wrong), the Ordinal-min fix = Min (correct).
    private static TaxonomyTreeDto CraftedTree() => new(
        Regions: [],
        OccupationFields:
        [
            new TaxonomyOccupationFieldDto(
                ConceptId: "field-A",
                Label: "Field A",
                Occupations: [new TaxonomyOccupationDto(OccupationConceptId, OccupationName)],
                OccupationGroups: [new TaxonomyOccupationGroupDto(GroupConceptId, OrdinalMaxLabel)]),
            new TaxonomyOccupationFieldDto(
                ConceptId: "field-B",
                Label: "Field B",
                Occupations: [],
                OccupationGroups: [new TaxonomyOccupationGroupDto(GroupConceptId, OrdinalMinLabel)]),
        ],
        EmploymentTypes: [],
        WorktimeExtents: []);

    [Fact]
    public async Task DeriveAsync_GroupIdUnderTwoFieldsWithDifferentLabels_PicksOrdinalMinimumLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync(OccupationName, ct);

        var exact = result.Candidates.Single(c =>
            c.OccupationGroupConceptId == GroupConceptId
            && c.MatchKind == OccupationMatchKind.ExactOccupationName);
        exact.OccupationGroupLabel.ShouldBe(
            OrdinalMinLabel,
            "a group id under two fields with diverging labels must resolve to the Ordinal-minimum "
            + "label deterministically, never the enumeration-order First (#373/#268, ADR 0040/0071)");
    }

    [Fact]
    public async Task DeriveAsync_GroupLabelTiebreak_IsReproducibleAcrossRuns()
    {
        var ct = TestContext.Current.CancellationToken;

        var first = (await NewDeriver().DeriveAsync(OccupationName, ct)).Candidates
            .Single(c => c.OccupationGroupConceptId == GroupConceptId
                && c.MatchKind == OccupationMatchKind.ExactOccupationName)
            .OccupationGroupLabel;
        var second = (await NewDeriver().DeriveAsync(OccupationName, ct)).Candidates
            .Single(c => c.OccupationGroupConceptId == GroupConceptId
                && c.MatchKind == OccupationMatchKind.ExactOccupationName)
            .OccupationGroupLabel;

        first.ShouldBe(OrdinalMinLabel);
        second.ShouldBe(first);
    }
}
