using FluentValidation.TestHelper;
using Jobbliggaren.Application.JobSeekers.Commands.AddPreferredSkills;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.AddPreferredSkills;

public class AddPreferredSkillsCommandValidatorTests
{
    private readonly AddPreferredSkillsCommandValidator _sut = new();

    [Fact]
    public void Validate_OneConceptGroup_IsValid() =>
        _sut.TestValidate(new AddPreferredSkillsCommand(["sk_c", "Abc-123_x"])).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validate_EmptyList_IsRejected() =>
        _sut.TestValidate(new AddPreferredSkillsCommand([])).ShouldHaveValidationErrorFor(c => c.ConceptIds);

    [Fact]
    public void Validate_NullList_IsRejected() =>
        _sut.TestValidate(new AddPreferredSkillsCommand(null!)).ShouldHaveValidationErrorFor(c => c.ConceptIds);

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("../etc")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33 characters
    public void Validate_MalformedConceptId_IsRejected(string conceptId) =>
        _sut.TestValidate(new AddPreferredSkillsCommand([conceptId])).IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_MoreIdsThanTheConceptBound_IsRejected()
    {
        var ids = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1).Select(i => $"sk_{i}").ToList();

        _sut.TestValidate(new AddPreferredSkillsCommand(ids)).ShouldHaveValidationErrorFor(c => c.ConceptIds);
    }
}
