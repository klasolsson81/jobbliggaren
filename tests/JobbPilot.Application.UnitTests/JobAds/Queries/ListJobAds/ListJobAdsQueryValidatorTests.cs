using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.SavedSearches;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.ListJobAds;

// C1 (ADR 0067 Platsbanken sök-paritet) — Variant C nivåbyte: dimensionerna
// OccupationGroup (→ ssyk-level-4, occupation_group_concept_id) + Municipality
// (→ municipality_concept_id). Samma per-element-regex + maxantal-cap-mönster
// som Region. Boundary-tester refererar SearchCriteria.MaxConceptIds, ALDRIG
// literalen 400 (DRY, CLAUDE.md §5.1).
//
// C2 (CTO-dom (e)): Ssyk-paramen + Ssyk-validator-reglerna är BORTTAGNA —
// ?ssyk= är numera en obunden query-param som Minimal-API-bindningen
// ignorerar (se ListJobAdsSsykNoOpTests för integrationsbeviset).
//
// RÖD tills ListJobAdsQuery + ListJobAdsQueryValidator droppat Ssyk.
public class ListJobAdsQueryValidatorTests
{
    private readonly ListJobAdsQueryValidator _validator = new();

    [Fact]
    public void Validate_WithDefaults_IsValid()
    {
        var result = _validator.Validate(new ListJobAdsQuery());
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageBelowOne_IsInvalid(int page)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Page: page));
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_PageSizeOutsideRange_IsInvalid(int pageSize)
    {
        var result = _validator.Validate(new ListJobAdsQuery(PageSize: pageSize));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SortByUnknownEnum_IsInvalid()
    {
        var result = _validator.Validate(new ListJobAdsQuery(SortBy: (JobAdSortBy)999));
        result.IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // OccupationGroup (NY dimension — primärt yrke-filter, Variant C)
    // Per-element regex ^[A-Za-z0-9_-]{1,32}$ via RuleForEach.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("MVqp_eS8_kDZ")]
    [InlineData("abc-123")]
    [InlineData("Z")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-12")] // 32 tecken (max)
    public void Validate_OccupationGroup_SingleValidConceptId_Passes(string group)
    {
        var result = _validator.Validate(new ListJobAdsQuery(OccupationGroup: [group]));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OccupationGroup_MultipleValidConceptIds_Passes()
    {
        var result = _validator.Validate(
            new ListJobAdsQuery(OccupationGroup: ["MVqp_eS8_kDZ", "abc-123", "Z"]));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("<script>")]
    [InlineData("dot.notation")]
    [InlineData("plus+sign")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")] // 33 tecken
    public void Validate_OccupationGroup_AnyInvalidElement_Fails(string bad)
    {
        var result = _validator.Validate(
            new ListJobAdsQuery(OccupationGroup: ["12345", bad]));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_OccupationGroup_Null_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(OccupationGroup: null));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OccupationGroup_EmptyList_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(OccupationGroup: []));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OccupationGroup_ExactlyMax_Passes()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToArray();
        var result = _validator.Validate(new ListJobAdsQuery(OccupationGroup: max));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OccupationGroup_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();
        var result = _validator.Validate(new ListJobAdsQuery(OccupationGroup: overMax));
        result.IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Municipality (NY dimension — kommun, barn under Region)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("MVqp_eS8_kDZ")]
    [InlineData("abc-123")]
    public void Validate_Municipality_SingleValidConceptId_Passes(string municipality)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Municipality: [municipality]));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Municipality_MultipleValidConceptIds_Passes()
    {
        var result = _validator.Validate(
            new ListJobAdsQuery(Municipality: ["sthlm_kn", "uppsala_kn"]));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("<script>")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")] // 33 tecken
    public void Validate_Municipality_AnyInvalidElement_Fails(string bad)
    {
        var result = _validator.Validate(
            new ListJobAdsQuery(Municipality: ["sthlm_kn", bad]));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Municipality_Null_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Municipality: null));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Municipality_ExactlyMax_Passes()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"kn{i}").ToArray();
        var result = _validator.Validate(new ListJobAdsQuery(Municipality: max));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Municipality_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn{i}").ToArray();
        var result = _validator.Validate(new ListJobAdsQuery(Municipality: overMax));
        result.IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Region — oförändrad dimension, cap höjd 10→400.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("MVqp_eS8_kDZ")]
    [InlineData("abc-123")]
    public void Validate_Region_SingleValidConceptId_Passes(string region)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Region: [region]));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Region_MultipleValidConceptIds_Passes()
    {
        var result = _validator.Validate(
            new ListJobAdsQuery(Region: ["stockholm", "uppsala"]));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("<script>")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")]
    public void Validate_Region_AnyInvalidElement_Fails(string bad)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Region: ["stockholm", bad]));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Region_Null_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Region: null));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Region_ExactlyMax_Passes()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"reg{i}").ToArray();
        var result = _validator.Validate(new ListJobAdsQuery(Region: max));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Region_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();
        var result = _validator.Validate(new ListJobAdsQuery(Region: overMax));
        result.IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Q oförändrat — 2-100 tecken
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_Q_TooShort_Fails()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Q: "a"));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Q_TooLong_Fails()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Q: new string('x', 101)));
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("developer")]
    public void Validate_Q_ValidLength_Passes(string q)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Q: q));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Q_AtMaxLength_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Q: new string('x', 100)));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Q_Null_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Q: null));
        result.IsValid.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Kombinerat
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_AllFiltersNull_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(
            Page: 1, PageSize: 20, SortBy: JobAdSortBy.PublishedAtDesc,
            OccupationGroup: null, Municipality: null, Region: null, Q: null));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AllFiltersValid_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(
            Page: 1, PageSize: 20, SortBy: JobAdSortBy.PublishedAtDesc,
            OccupationGroup: ["grp-1", "grp-2"], Municipality: ["sthlm_kn"],
            Region: ["stockholm"], Q: "developer"));
        result.IsValid.ShouldBeTrue();
    }

    // ADR 0042 Beslut D — Relevance kräver Q (speglar SearchCriteria-invarianten).

    [Fact]
    public void Validate_RelevanceSortWithoutQ_IsInvalid()
    {
        var result = _validator.Validate(new ListJobAdsQuery(
            SortBy: JobAdSortBy.Relevance, OccupationGroup: ["12345"], Q: null));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RelevanceSortWithQ_IsValid()
    {
        var result = _validator.Validate(new ListJobAdsQuery(
            SortBy: JobAdSortBy.Relevance, Q: "developer"));
        result.IsValid.ShouldBeTrue();
    }
}
