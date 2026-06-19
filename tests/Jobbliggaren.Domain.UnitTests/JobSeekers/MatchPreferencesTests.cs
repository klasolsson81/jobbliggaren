using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

// F4-12 (CTO-frozen) — MatchPreferences VO på JobSeeker-aggregatet bär
// användarens STATED jobbsöks-preferenser (yrkesgrupper, regioner,
// anställningsformer). Speglar SearchCriteria:s normalisering + strukturella
// likhet (ADR 0042 Beslut B) för jsonb-collection-equality, MED ETT MEDVETET
// AVSTEG: tom-invarianten finns INTE här — alla tre listorna tomma är en
// GILTIG MatchPreferences (en användare som ännu inte angett preferenser).
// Per-element-format + per-list-cap återanvänder SearchCriteria-kontraktet
// (concept-id-regex ^[A-Za-z0-9_-]{1,32}$, MaxConceptIds = 400).
//
// RÖD tills MatchPreferences.cs implementeras (typen finns inte ännu) — TDD-
// RED. Kompilerar mot mål-API:t (Create-signatur + properties) så att impl-
// bygget blockeras tills produktionstypen finns.
//
// ANTAGANDE (att verifiera av Klas/impl): MatchPreferences.Create returnerar
// Result<MatchPreferences> med DomainError (speglar SearchCriteria.Create
// exakt) och felkoder med prefix "MatchPreferences." per dimension. Om impl
// väljer en annan signalering (t.ex. throw) faller dessa tester och signalen
// är att kontraktet behöver bekräftas.
public class MatchPreferencesTests
{
    // Helper — named args obligatoriskt (tre likatypade listor i rad,
    // architect-disciplin speglad från SearchCriteriaTests).
    private static Result<MatchPreferences> Create(
        IEnumerable<string>? preferredOccupationGroups = null,
        IEnumerable<string>? preferredRegions = null,
        IEnumerable<string>? preferredEmploymentTypes = null) =>
        MatchPreferences.Create(
            preferredOccupationGroups: preferredOccupationGroups,
            preferredRegions: preferredRegions,
            preferredEmploymentTypes: preferredEmploymentTypes);

    // ---------------------------------------------------------------
    // Happy path — varje dimension samt allihop
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithOccupationGroupsOnly_ReturnsSuccess()
    {
        var result = Create(preferredOccupationGroups: ["grp_12345"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithRegionsOnly_ReturnsSuccess()
    {
        var result = Create(preferredRegions: ["stockholm_AB"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredRegions.ShouldBe(["stockholm_AB"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithEmploymentTypesOnly_ReturnsSuccess()
    {
        var result = Create(preferredEmploymentTypes: ["et_fast"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllThreeDimensions_ReturnsSuccess()
    {
        var result = Create(
            preferredOccupationGroups: ["grp1", "grp2"],
            preferredRegions: ["stockholm", "uppsala"],
            preferredEmploymentTypes: ["et_fast", "et_vikariat"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["grp1", "grp2"]);
        result.Value.PreferredRegions.ShouldBe(["stockholm", "uppsala"]);
        result.Value.PreferredEmploymentTypes.ShouldBe(["et_fast", "et_vikariat"]);
    }

    // ---------------------------------------------------------------
    // MEDVETET AVSTEG mot SearchCriteria — EMPTY ÄR GILTIGT.
    // Ingen "minst ett kriterium"-invariant. En användare utan angivna
    // preferenser har en giltig (tom) MatchPreferences.
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithAllEmpty_ReturnsSuccess()
    {
        var result = Create();

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllExplicitlyEmptyLists_ReturnsSuccess()
    {
        var result = Create(
            preferredOccupationGroups: [],
            preferredRegions: [],
            preferredEmploymentTypes: []);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithOnlyWhitespaceElements_NormalizesToEmpty_AndStaysValid()
    {
        // Whitespace-element droppas i normaliseringen → tom lista → fortfarande
        // GILTIG (till skillnad från SearchCriteria.Empty).
        var result = Create(
            preferredOccupationGroups: ["", "  "],
            preferredRegions: [" "],
            preferredEmploymentTypes: ["\t"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBeEmpty();
        result.Value.PreferredRegions.ShouldBeEmpty();
        result.Value.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_IsAValidNoneInstance_WithAllListsEmpty()
    {
        // ANTAGANDE: VO:t exponerar en statisk Empty/None-instans (parity med
        // hur SearchCriteria saknar en sådan men MatchPreferences behöver den
        // som honest "inga preferenser angivna"-default). Om impl väljer ett
        // annat namn faller detta och kontraktet behöver bekräftas.
        var none = MatchPreferences.Empty;

        none.PreferredOccupationGroups.ShouldBeEmpty();
        none.PreferredRegions.ShouldBeEmpty();
        none.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_EqualsCreateWithAllEmpty()
    {
        MatchPreferences.Empty.ShouldBe(Create().Value);
    }

    // ---------------------------------------------------------------
    // Normalisering — trim + droppa tom/whitespace + distinct ordinal +
    // sorterad ordinal (identiskt med SearchCriteria.NormalizeList).
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesOccupationGroups_SortedDistinctOrdinal()
    {
        var result = Create(preferredOccupationGroups: ["b", "a", "b", " c "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Create_NormalizesRegions_SortedDistinctOrdinal()
    {
        var result = Create(preferredRegions: ["uppsala", "stockholm", "uppsala"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredRegions.ShouldBe(["stockholm", "uppsala"]);
    }

    [Fact]
    public void Create_NormalizesEmploymentTypes_SortedDistinctOrdinal()
    {
        var result = Create(preferredEmploymentTypes: ["et_vikariat", "et_fast", "et_fast"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredEmploymentTypes.ShouldBe(["et_fast", "et_vikariat"]);
    }

    [Fact]
    public void Create_DropsEmptyAndWhitespaceElements_KeepsValidOnes()
    {
        var result = Create(preferredOccupationGroups: ["grp1", "", "   ", "grp2"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe(["grp1", "grp2"]);
    }

    [Fact]
    public void Create_OrdinalSort_IsCaseSensitive()
    {
        var a = Create(preferredOccupationGroups: ["zebra", "Apple"]).Value;
        var b = Create(preferredOccupationGroups: ["Apple", "zebra"]).Value;

        a.PreferredOccupationGroups.ShouldBe(b.PreferredOccupationGroups);
        // ordinal: 'A' (65) < 'z' (122)
        a.PreferredOccupationGroups[0].ShouldBe("Apple");
    }

    // ---------------------------------------------------------------
    // Strukturell likhet — Equals + GetHashCode i kanonisk dimensionsordning
    // (OccupationGroups, Regions, EmploymentTypes). Krävs för jsonb-collection-
    // equality (record + IReadOnlyList får annars referens-equality).
    // ---------------------------------------------------------------

    [Fact]
    public void TwoPreferences_SameElementsDifferentOrder_AreValueEqual()
    {
        var a = Create(
            preferredOccupationGroups: ["b", "a"],
            preferredRegions: ["y", "x"],
            preferredEmploymentTypes: ["et_b", "et_a"]).Value;
        var b = Create(
            preferredOccupationGroups: ["a", "b"],
            preferredRegions: ["x", "y"],
            preferredEmploymentTypes: ["et_a", "et_b"]).Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldBe(b);
    }

    [Fact]
    public void TwoPreferences_DifferentOccupationGroups_AreNotValueEqual()
    {
        var a = Create(preferredOccupationGroups: ["grp1"]).Value;
        var b = Create(preferredOccupationGroups: ["grp9"]).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_DifferentRegions_AreNotValueEqual()
    {
        var a = Create(preferredRegions: ["stockholm"]).Value;
        var b = Create(preferredRegions: ["uppsala"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_DifferentEmploymentTypes_AreNotValueEqual()
    {
        var a = Create(preferredEmploymentTypes: ["et_fast"]).Value;
        var b = Create(preferredEmploymentTypes: ["et_vikariat"]).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoPreferences_SameValueInDifferentDimension_AreNotValueEqual()
    {
        // Dimension-förväxlingsgrind: samma concept-id i OLIKA dimensioner får
        // ALDRIG vara lika (jsonb-dedupe/equality-säkerhet).
        var a = Create(preferredOccupationGroups: ["x1"]).Value;
        var b = Create(preferredRegions: ["x1"]).Value;
        var c = Create(preferredEmploymentTypes: ["x1"]).Value;

        a.ShouldNotBe(b);
        b.ShouldNotBe(c);
        a.ShouldNotBe(c);
    }

    [Fact]
    public void TrimNormalized_AreValueEqualToUntrimmed()
    {
        var a = Create(preferredOccupationGroups: ["  grp1  "]).Value;
        var b = Create(preferredOccupationGroups: ["grp1"]).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void DuplicateElements_NormalizeToSame_AreValueEqual()
    {
        var a = Create(preferredRegions: ["stockholm", "stockholm"]).Value;
        var b = Create(preferredRegions: ["stockholm"]).Value;

        a.ShouldBe(b);
    }

    // ---------------------------------------------------------------
    // Maxantal-cap = SearchCriteria.MaxConceptIds (ÅTERANVÄND KONSTANTEN —
    // refererar aldrig literalen 400, så testet följer med vid framtida ändring).
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithExactlyMaxOccupationGroups_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToArray();

        var result = Create(preferredOccupationGroups: max);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxOccupationGroups_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();

        var result = Create(preferredOccupationGroups: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyOccupationGroups");
    }

    [Fact]
    public void Create_WithOneOverMaxRegions_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();

        var result = Create(preferredRegions: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyRegions");
    }

    [Fact]
    public void Create_WithOneOverMaxEmploymentTypes_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"et{i}").ToArray();

        var result = Create(preferredEmploymentTypes: overMax);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManyEmploymentTypes");
    }

    [Fact]
    public void Create_CapAppliesAfterDistinct_MaxPlusOneWithDuplicateUnderCap_ReturnsSuccess()
    {
        // Cap appliceras EFTER distinct-normaliseringen (paritet SearchCriteria).
        var raw = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToList();
        raw.Add("grp1"); // dubblett

        var result = Create(preferredOccupationGroups: raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    // ---------------------------------------------------------------
    // Per-element regex ^[A-Za-z0-9_-]{1,32}$ per dimension (default-deny,
    // speglar SearchCriteria.ConceptIdPattern).
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("semi;colon")]
    [InlineData("dot.notation")]
    [InlineData("plus+sign")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken > 32
    public void Create_WithInvalidOccupationGroupElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredOccupationGroups: ["grp1", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidOccupationGroup");
    }

    [Theory]
    [InlineData("region space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidRegionElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredRegions: ["stockholm", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidRegion");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("dot.notation")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidEmploymentTypeElement_ReturnsFailure(string bad)
    {
        var result = Create(preferredEmploymentTypes: ["et_fast", bad]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidEmploymentType");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ABC-123_xyz")]
    [InlineData("12345678901234567890123456789012")] // exakt 32 tecken
    public void Create_WithValidElementFormat_ReturnsSuccess(string conceptId)
    {
        var result = Create(preferredOccupationGroups: [conceptId]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreferredOccupationGroups.ShouldBe([conceptId]);
    }
}
