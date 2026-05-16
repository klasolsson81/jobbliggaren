using JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.SuggestJobAdTerms;

// Batch 5 — ADR 0042 Beslut C. DoS-floor (min prefix ≥2, Limit-cap 1-20)
// enforce:as i Validation-pipeline FÖRE handlern. Speglar
// ListJobAdsQueryValidator-mönstret.
public class SuggestJobAdTermsQueryValidatorTests
{
    private readonly SuggestJobAdTermsQueryValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void Validate_PrefixBelowTwoChars_IsInvalid(string prefix)
    {
        var result = _validator.Validate(new SuggestJobAdTermsQuery(prefix));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_PrefixOver100Chars_IsInvalid()
    {
        var result = _validator.Validate(
            new SuggestJobAdTermsQuery(new string('x', 101)));
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Validate_LimitOutOfRange_IsInvalid(int limit)
    {
        var result = _validator.Validate(new SuggestJobAdTermsQuery("ut", limit));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ValidPrefixAndLimit_Passes()
    {
        var result = _validator.Validate(new SuggestJobAdTermsQuery("utvecklare", 10));
        result.IsValid.ShouldBeTrue();
    }
}
