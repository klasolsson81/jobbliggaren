using Jobbliggaren.Application.JobAds.Queries.DeriveOccupationCodes;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.DeriveOccupationCodes;

// Fas 4 STEG 3 (F4-3) — DoS-/garbage-floor enforce:as i Validation-pipeline FÖRE
// handlern (query körs aldrig med tom eller orimligt lång titel). Speglar
// SuggestJobAdTermsQueryValidatorTests: Title NotEmpty + MaximumLength(100)
// (CTO Decision 5 — sane cap, paritet SuggestJobAdTermsQueryValidator.Prefix).
//
// RED tills DeriveOccupationCodesQuery + DeriveOccupationCodesQueryValidator finns.
public class DeriveOccupationCodesQueryValidatorTests
{
    private readonly DeriveOccupationCodesQueryValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Validate_TitleEmptyOrWhitespace_IsInvalid(string title)
    {
        // NotEmpty underkänner både "" och ren whitespace (FluentValidation
        // NotEmpty trimmar inte men whitespace-only räknas som tomt här via
        // standard NotEmpty-semantiken på string).
        var result = _validator.Validate(new DeriveOccupationCodesQuery(title));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_TitleOver100Chars_IsInvalid()
    {
        var result = _validator.Validate(
            new DeriveOccupationCodesQuery(new string('x', 101)));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_TitleExactly100Chars_Passes()
    {
        // Gräns-värdet (MaximumLength(100) är inklusivt) ska passera.
        var result = _validator.Validate(
            new DeriveOccupationCodesQuery(new string('x', 100)));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Advokat")]
    [InlineData("Systemutvecklare")]
    [InlineData("Förskollärare")]
    public void Validate_ValidTitle_Passes(string title)
    {
        var result = _validator.Validate(new DeriveOccupationCodesQuery(title));
        result.IsValid.ShouldBeTrue();
    }
}
