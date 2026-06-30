using FluentValidation.TestHelper;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class FollowCompanyCommandValidatorTests
{
    private readonly FollowCompanyCommandValidator _validator = new();

    [Theory]
    [InlineData("5592804784")]
    [InlineData("9001011234")] // personnummer-shaped — still a valid 10-digit input (guard is at surfacing, not here)
    public void Valid_TenDigit_OrgNumber_Passes(string orgNr)
    {
        _validator.TestValidate(new FollowCompanyCommand(orgNr))
            .ShouldNotHaveValidationErrorFor(c => c.OrganizationNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("559280478")]    // 9 digits
    [InlineData("55928047840")]  // 11 digits
    [InlineData("559280478X")]   // non-digit
    [InlineData("559280-4784")]  // hyphen
    public void Invalid_OrgNumber_Fails(string orgNr)
    {
        _validator.TestValidate(new FollowCompanyCommand(orgNr))
            .ShouldHaveValidationErrorFor(c => c.OrganizationNumber);
    }
}
