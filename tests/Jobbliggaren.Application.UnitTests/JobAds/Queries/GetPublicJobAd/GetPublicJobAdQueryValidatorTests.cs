using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAd;

public class GetPublicJobAdQueryValidatorTests
{
    private readonly GetPublicJobAdQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyId_Fails() =>
        _validator.Validate(new GetPublicJobAdQuery(Guid.Empty)).IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_Id_Passes() =>
        _validator.Validate(new GetPublicJobAdQuery(Guid.NewGuid())).IsValid.ShouldBeTrue();
}
