using Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

public class MarkFollowedCompanyAdSeenCommandValidatorTests
{
    private readonly MarkFollowedCompanyAdSeenCommandValidator _validator = new();

    [Fact]
    public void Validate_WithNonEmptyJobAdId_Passes()
    {
        var result = _validator.Validate(new MarkFollowedCompanyAdSeenCommand(Guid.NewGuid()));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithEmptyJobAdId_Fails()
    {
        var result = _validator.Validate(new MarkFollowedCompanyAdSeenCommand(Guid.Empty));
        result.IsValid.ShouldBeFalse();
    }
}
