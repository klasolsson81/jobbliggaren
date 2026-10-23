using Jobbliggaren.Application.JobAds.Queries.SearchOccupations;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.SearchOccupations;

// #2198 (ADR 0180) — the validator measures the TRIMMED query against 2..100 and its messages
// interpolate only the constants, so a 400 body can never echo what the user typed.
public class SearchOccupationsQueryValidatorTests
{
    private readonly SearchOccupationsQueryValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("  a  ")]
    public void Validate_TrimmedQueryShorterThanTwo_IsInvalid(string query)
    {
        _validator.Validate(new SearchOccupationsQuery(query)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_TrimmedQueryOfExactlyMaxLength_Passes()
    {
        var query = "  " + new string('x', SearchOccupationsQuery.MaxQueryLength) + "  ";

        _validator.Validate(new SearchOccupationsQuery(query)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_TrimmedQueryOverMaxLength_IsInvalid()
    {
        var query = new string('x', SearchOccupationsQuery.MaxQueryLength + 1);

        _validator.Validate(new SearchOccupationsQuery(query)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ux")]
    [InlineData("projektledare")]
    [InlineData("Projektledare, IT")]
    public void Validate_ValidQuery_Passes(string query)
    {
        _validator.Validate(new SearchOccupationsQuery(query)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Messages_NeverEchoTheQuery()
    {
        // A canary no fixed message can contain by accident: a word over the bound and a lone sign. The
        // check looks for the canary itself, so a message that echoed a prefix of the query is caught too.
        const string canary = "kanariefågel";
        var tooLong = canary + new string('z', SearchOccupationsQuery.MaxQueryLength);
        const string tooShort = "§";

        foreach (var (query, mark) in new[] { (tooLong, canary), (tooShort, tooShort) })
        {
            var result = _validator.Validate(new SearchOccupationsQuery(query));

            result.IsValid.ShouldBeFalse();
            result.Errors.ShouldAllBe(e => !e.ErrorMessage.Contains(mark, StringComparison.Ordinal));
        }
    }
}
