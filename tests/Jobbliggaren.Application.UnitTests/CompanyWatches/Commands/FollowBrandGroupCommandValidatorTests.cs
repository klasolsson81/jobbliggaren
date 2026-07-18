using FluentValidation.TestHelper;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowBrandGroup;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class FollowBrandGroupCommandValidatorTests
{
    private readonly FollowBrandGroupCommandValidator _validator = new();

    [Theory]
    [InlineData("volvo-koncernen")]
    [InlineData("volvo")]
    [InlineData("group2")]
    public void Valid_slug_passes(string slug)
    {
        _validator.TestValidate(new FollowBrandGroupCommand(slug))
            .ShouldNotHaveValidationErrorFor(c => c.BrandGroupId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Volvo")]         // uppercase
    [InlineData("volvo koncern")] // space
    [InlineData("volvo_koncern")] // underscore
    public void Malformed_slug_fails(string slug)
    {
        _validator.TestValidate(new FollowBrandGroupCommand(slug))
            .ShouldHaveValidationErrorFor(c => c.BrandGroupId);
    }

    [Fact]
    public void Format_only_a_wellformed_but_uncurated_slug_passes_the_validator()
    {
        // Existence is the HANDLER's concern (a 404), not the validator's (a 400) — a well-formed but
        // uncurated slug must clear format validation so the handler can return NotFound.
        _validator.TestValidate(new FollowBrandGroupCommand("not-a-real-group"))
            .IsValid.ShouldBeTrue();
    }
}
