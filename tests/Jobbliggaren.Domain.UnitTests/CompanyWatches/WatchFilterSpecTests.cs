using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// Bevaknings-reconcile RF-2 (senior-cto-advisor 2026-07-12) — invariants for
/// <see cref="WatchFilterSpec"/>: the empty-spec invariant (NULL column is the only canonical
/// "no filter"), normalization (sorted+distinct ordinal), per-element concept-id format,
/// the SearchCriteria.MaxConceptIds cap, structural equality (jsonb value comparison relies
/// on it), and the AdmitsMunicipality ort semantics (8A: NULL-municipality ads never pass an
/// active ort filter).
/// </summary>
public class WatchFilterSpecTests
{
    // ---------------------------------------------------------------
    // Create — empty-spec invariant + valid combinations
    // ---------------------------------------------------------------

    [Fact]
    public void Create_EmptyMunicipalitiesAndOnlyMatchedFalse_Fails()
    {
        var result = WatchFilterSpec.Create(municipalities: null, onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.Empty");
    }

    [Fact]
    public void Create_WhitespaceOnlyMunicipalitiesAndOnlyMatchedFalse_Fails()
    {
        var result = WatchFilterSpec.Create(["  ", ""], onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.Empty");
    }

    [Fact]
    public void Create_OnlyMatchedAlone_Succeeds()
    {
        var result = WatchFilterSpec.Create(municipalities: null, onlyMatched: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipalities.ShouldBeEmpty();
        result.Value.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public void Create_MunicipalitiesAlone_Succeeds()
    {
        var result = WatchFilterSpec.Create(["1gEC_kvM_TXK"], onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Municipalities.ShouldBe(["1gEC_kvM_TXK"]);
        result.Value.OnlyMatched.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Normalization — trim, drop blank, distinct + sort ordinal
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesMunicipalities_TrimDistinctSortedOrdinal()
    {
        var result = WatchFilterSpec.Create(
            [" zzz_id ", "aaa_id", "zzz_id", "", "  ", "AAA_id"], onlyMatched: false);

        result.IsSuccess.ShouldBeTrue();
        // Ordinal sort: uppercase before lowercase.
        result.Value.Municipalities.ShouldBe(["AAA_id", "aaa_id", "zzz_id"]);
    }

    // ---------------------------------------------------------------
    // Cap + per-element format (default-deny)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_MoreThanMaxConceptIdsMunicipalities_Fails()
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"id_{i}");

        var result = WatchFilterSpec.Create(tooMany, onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.TooManyMunicipalities");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö_id")]
    [InlineData("way_too_long_for_a_concept_id_over_32_chars")]
    [InlineData("semi;colon")]
    public void Create_InvalidConceptId_Fails(string invalid)
    {
        var result = WatchFilterSpec.Create([invalid], onlyMatched: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("WatchFilterSpec.InvalidMunicipality");
    }

    // ---------------------------------------------------------------
    // Structural equality (jsonb value-comparison footgun)
    // ---------------------------------------------------------------

    [Fact]
    public void Equals_LogicallyEqualSpecs_AreStructurallyEqual()
    {
        var a = WatchFilterSpec.Create(["bbb", " aaa "], onlyMatched: true).Value;
        var b = WatchFilterSpec.Create(["aaa", "bbb", "bbb"], onlyMatched: true).Value;

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentSpecs_AreNotEqual()
    {
        var ortOnly = WatchFilterSpec.Create(["aaa"], onlyMatched: false).Value;
        var ortAndMatched = WatchFilterSpec.Create(["aaa"], onlyMatched: true).Value;
        var otherOrt = WatchFilterSpec.Create(["bbb"], onlyMatched: false).Value;

        ortOnly.Equals(ortAndMatched).ShouldBeFalse();
        ortOnly.Equals(otherOrt).ShouldBeFalse();
        ortOnly.Equals(null).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // AdmitsMunicipality — ort semantics (8A)
    // ---------------------------------------------------------------

    [Fact]
    public void AdmitsMunicipality_NoOrtFilter_AdmitsEverything()
    {
        var spec = WatchFilterSpec.Create(municipalities: null, onlyMatched: true).Value;

        spec.AdmitsMunicipality("any_id").ShouldBeTrue();
        spec.AdmitsMunicipality(null).ShouldBeTrue();
    }

    [Fact]
    public void AdmitsMunicipality_ActiveOrtFilter_AdmitsOnlyListedMunicipalities()
    {
        var spec = WatchFilterSpec.Create(["kommun_a", "kommun_b"], onlyMatched: false).Value;

        spec.AdmitsMunicipality("kommun_a").ShouldBeTrue();
        spec.AdmitsMunicipality("kommun_c").ShouldBeFalse();
    }

    [Fact]
    public void AdmitsMunicipality_ActiveOrtFilter_RejectsNullMunicipality()
    {
        // 8A stance: a län-only ad (no municipality concept-id) never matches an
        // active ort filter — the user chose specific municipalities.
        var spec = WatchFilterSpec.Create(["kommun_a"], onlyMatched: false).Value;

        spec.AdmitsMunicipality(null).ShouldBeFalse();
    }
}
