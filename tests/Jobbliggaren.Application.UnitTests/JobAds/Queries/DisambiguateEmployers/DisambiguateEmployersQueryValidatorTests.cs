using Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.DisambiguateEmployers;

// ADR 0087 D6 (#311 PR-2b C2) — the validator is pre-handler defense-in-depth: the company-name
// term must be a TRIMMED 2–100 chars (an empty/all-whitespace/1-char term is a clean 400 rather
// than a near-whole-corpus ILIKE scan). Length is measured on the trimmed value (the handler trims
// before the ILIKE). There is NO org.nr-format rule — the input is a NAME, not an org.nr.
public class DisambiguateEmployersQueryValidatorTests
{
    private readonly DisambiguateEmployersQueryValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("a")]      // 1 char
    [InlineData("  a  ")]  // trims to 1
    public void Validate_WhenTrimmedShorterThanTwo_IsInvalid(string query)
    {
        _validator.Validate(new DisambiguateEmployersQuery(query)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    [InlineData("Volvo")]
    public void Validate_WhenTrimmedTwoOrMore_IsValid(string query)
    {
        _validator.Validate(new DisambiguateEmployersQuery(query)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AtMaxLength_IsValid()
    {
        var query = new string('x', DisambiguateEmployersQuery.MaxQueryLength);
        _validator.Validate(new DisambiguateEmployersQuery(query)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverMaxLength_IsInvalid()
    {
        var query = new string('x', DisambiguateEmployersQuery.MaxQueryLength + 1);
        _validator.Validate(new DisambiguateEmployersQuery(query)).IsValid.ShouldBeFalse();
    }
}
