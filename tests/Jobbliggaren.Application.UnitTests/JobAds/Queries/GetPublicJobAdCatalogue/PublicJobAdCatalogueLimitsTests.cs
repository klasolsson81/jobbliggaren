using FluentValidation.TestHelper;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// The depth cap is derived from the snapshot's ceiling (ADR 0176 F2): it must reach every ad the snapshot can hold, and
/// no further, so it bounds the URL space and never the listed set.
/// </summary>
public class PublicJobAdCatalogueLimitsTests
{
    private readonly GetPublicJobAdCatalogueQueryValidator _validator = new();

    [Fact]
    public void MaxPage_ReachesEveryAdTheSnapshotCanHold() =>
        (PublicJobAdCatalogueLimits.MaxPage * PublicJobAdCatalogueLimits.PageSize)
            .ShouldBeGreaterThanOrEqualTo(ListedJobAdSnapshotLimits.Ceiling);

    [Fact]
    public void MaxPage_IsNoDeeperThanTheCeilingNeeds() =>
        ((PublicJobAdCatalogueLimits.MaxPage - 1) * PublicJobAdCatalogueLimits.PageSize)
            .ShouldBeLessThan(ListedJobAdSnapshotLimits.Ceiling);

    [Theory]
    [InlineData(1)]
    [InlineData(PublicJobAdCatalogueLimits.MaxPage)]
    public void Validate_APageInsideTheCap_IsValid(int page) =>
        _validator.TestValidate(new GetPublicJobAdCatalogueQuery(page)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PublicJobAdCatalogueLimits.MaxPage + 1)]
    public void Validate_APageOutsideTheCap_IsInvalid(int page) =>
        _validator.TestValidate(new GetPublicJobAdCatalogueQuery(page)).ShouldHaveValidationErrorFor(q => q.Page);
}
