using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.SearchSkills;

// ADR 0079 STEG 3 PR-C — the typeahead validator is deliberately lenient: blank /
// short queries are VALID (return empty), only the MaximumLength DoS bound rejects.
public class SearchSkillsQueryValidatorTests
{
    private readonly SearchSkillsQueryValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("j")]
    [InlineData("java")]
    public void Validate_BlankShortOrNormalQuery_Passes(string query)
    {
        _validator.Validate(new SearchSkillsQuery(query)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverMaxLength_IsInvalid()
    {
        var tooLong = new string('a', 81);

        _validator.Validate(new SearchSkillsQuery(tooLong)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ExactlyMaxLength_Passes()
    {
        var atMax = new string('a', 80);

        _validator.Validate(new SearchSkillsQuery(atMax)).IsValid.ShouldBeTrue();
    }
}
