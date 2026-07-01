using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class FollowCompanyFromJobAdCommandValidatorTests
{
    private readonly FollowCompanyFromJobAdCommandValidator _validator = new();

    [Fact]
    public void Validate_WithNonEmptyJobAdId_Passes()
    {
        var result = _validator.Validate(new FollowCompanyFromJobAdCommand(Guid.NewGuid()));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithEmptyJobAdId_Fails()
    {
        var result = _validator.Validate(new FollowCompanyFromJobAdCommand(Guid.Empty));
        result.IsValid.ShouldBeFalse();
    }
}
