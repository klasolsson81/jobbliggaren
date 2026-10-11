using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Taxonomy;

/// <summary>
/// #2143 (ADR 0167) — the exact-label index behind <c>ITaxonomyLabelMatcher</c>, through its internal
/// seam: a minus-word names a concept only when it equals the concept's label, case aside.
/// No DB; the snapshot itself is exercised in <see cref="TaxonomyReadModelIntegrationTests"/>.
/// </summary>
public sealed class TaxonomyReadModelExactLabelTests
{
    private static TaxonomyConcept Concept(
        string id, TaxonomyConceptKind kind, string label, string? parent = null) =>
        new() { ConceptId = id, Kind = kind, Label = label, ParentConceptId = parent };

    private static readonly List<TaxonomyConcept> Concepts =
    [
        Concept("wt_del", TaxonomyConceptKind.WorktimeExtent, "Deltid"),
        Concept("et_beh", TaxonomyConceptKind.EmploymentType, "Behovsanställning"),
        Concept("reg_sth", TaxonomyConceptKind.Region, "Stockholms län"),
        Concept("mun_sth", TaxonomyConceptKind.Municipality, "Stockholm", "reg_sth"),
        Concept("fld_ped", TaxonomyConceptKind.OccupationField, "Pedagogik"),
        Concept("grp_lar", TaxonomyConceptKind.OccupationGroup, "Grundskollärare", "fld_ped"),
        Concept("grp_fsk", TaxonomyConceptKind.OccupationGroup, "Förskollärare", "fld_ped"),
        Concept("grp_ako", TaxonomyConceptKind.OccupationGroup, "Åklagare"),
        Concept("occ_ako", TaxonomyConceptKind.Occupation, "Åklagare"),
    ];

    private static readonly Dictionary<string, IReadOnlyList<TaxonomyReadModel.ExactLabelHit>> Index =
        TaxonomyReadModel.BuildExactLabelIndex(Concepts);

    private static IReadOnlyList<TaxonomyReadModel.ExactLabelHit> Hits(string term) =>
        Index.TryGetValue(term, out var hits) ? hits : [];

    [Theory]
    [InlineData("Deltid")]
    [InlineData("deltid")]
    [InlineData("DELTID")]
    public void ALabel_IsNamedCaseInsensitively(string term)
    {
        Hits(term).ShouldBe([new(TaxonomyConceptKind.WorktimeExtent, "wt_del")]);
    }

    [Theory]
    [InlineData("deltidsjobb")]
    [InlineData("delti")]
    [InlineData("stockholms")]
    public void APrefixOrAnExtension_NamesNothing(string term)
    {
        Hits(term).ShouldBeEmpty();
    }

    [Fact]
    public void AnOccupationField_IsIndexedAsItsGroups()
    {
        Hits("pedagogik").ShouldBe(
            [
                new(TaxonomyConceptKind.OccupationGroup, "grp_lar"),
                new(TaxonomyConceptKind.OccupationGroup, "grp_fsk"),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void ALabelSharedByAGroupAndAnOccupation_NamesBoth()
    {
        Hits("åklagare").ShouldBe(
            [
                new(TaxonomyConceptKind.OccupationGroup, "grp_ako"),
                new(TaxonomyConceptKind.Occupation, "occ_ako"),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void ARegion_IsNotIndexed()
    {
        Index.Values.SelectMany(h => h).ShouldNotContain(h => h.Kind == TaxonomyConceptKind.Region);
    }

    [Fact]
    public void AMunicipalityAndAnEmploymentType_AreIndexed()
    {
        Hits("stockholm").ShouldBe([new(TaxonomyConceptKind.Municipality, "mun_sth")]);
        Hits("behovsanställning").ShouldBe([new(TaxonomyConceptKind.EmploymentType, "et_beh")]);
    }

    // The index is built inside the read model's load: a kind it does not classify would fail every
    // taxonomy read, not only the minus-words.
    [Fact]
    public void EveryConceptKind_IsClassifiedByTheIndex()
    {
        var oneOfEach = Enum.GetValues<TaxonomyConceptKind>()
            .Select(kind => Concept($"id_{kind}", kind, $"Label {kind}"))
            .ToList();

        Should.NotThrow(() => TaxonomyReadModel.BuildExactLabelIndex(oneOfEach));
    }
}
