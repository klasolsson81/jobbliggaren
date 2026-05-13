using JobbPilot.Application.JobAds.Queries.ListJobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.ListJobAds;

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

    // F2-P9 (TD-70, CTO-rond 2026-05-13 Q7a/Q7b). Concept-id-pattern är
    // ^[A-Za-z0-9_-]{1,32}$ — defense-in-depth mot icke-JobTech-format.

    [Theory]
    [InlineData("MVqp_eS8_kDZ")] // typiskt JobTech-id
    [InlineData("abc-123")]
    [InlineData("Z")] // 1 tecken (minimum)
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-12")] // 32 tecken (max)
    public void Validate_Ssyk_ValidConceptId_Passes(string ssyk)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Ssyk: ssyk));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("<script>")]
    [InlineData("dot.notation")]
    [InlineData("plus+sign")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")] // 33 tecken
    public void Validate_Ssyk_InvalidFormat_Fails(string ssyk)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Ssyk: ssyk));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Ssyk_Null_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Ssyk: null));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Ssyk_WhitespaceOrEmpty_Passes(string? ssyk)
    {
        // IsNullOrWhiteSpace-bypass i validator → whitespace/empty skippar regex.
        var result = _validator.Validate(new ListJobAdsQuery(Ssyk: ssyk));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("MVqp_eS8_kDZ")]
    [InlineData("abc-123")]
    [InlineData("Z")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-12")]
    public void Validate_Region_ValidConceptId_Passes(string region)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Region: region));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("<script>")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")]
    public void Validate_Region_InvalidFormat_Fails(string region)
    {
        var result = _validator.Validate(new ListJobAdsQuery(Region: region));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Region_Null_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(Region: null));
        result.IsValid.ShouldBeTrue();
    }

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
    [InlineData("ab")] // 2 tecken (minimum)
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

    [Fact]
    public void Validate_AllFiltersNull_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(
            Page: 1, PageSize: 20, SortBy: JobAdSortBy.PublishedAtDesc,
            Ssyk: null, Region: null, Q: null));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AllFiltersValid_Passes()
    {
        var result = _validator.Validate(new ListJobAdsQuery(
            Page: 1, PageSize: 20, SortBy: JobAdSortBy.PublishedAtDesc,
            Ssyk: "MVqp_eS8_kDZ", Region: "abc-123", Q: "developer"));
        result.IsValid.ShouldBeTrue();
    }
}
