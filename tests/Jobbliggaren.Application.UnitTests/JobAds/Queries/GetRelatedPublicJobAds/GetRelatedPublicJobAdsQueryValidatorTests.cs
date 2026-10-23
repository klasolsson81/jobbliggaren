using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetRelatedPublicJobAds;

public class GetRelatedPublicJobAdsQueryValidatorTests
{
    private readonly GetRelatedPublicJobAdsQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyId_Fails() =>
        _validator.Validate(new GetRelatedPublicJobAdsQuery(Guid.Empty)).IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_Id_Passes() =>
        _validator.Validate(new GetRelatedPublicJobAdsQuery(Guid.NewGuid())).IsValid.ShouldBeTrue();
}
