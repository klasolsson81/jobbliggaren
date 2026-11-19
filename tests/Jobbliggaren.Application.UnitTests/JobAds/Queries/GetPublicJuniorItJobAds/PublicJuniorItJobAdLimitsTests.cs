using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>The junior IT list's page size, depth cap, kommun-id cap and its two orders (ADR 0186).</summary>
public class PublicJuniorItJobAdLimitsTests
{
    [Fact]
    public void PageSize_Is20() => PublicJuniorItJobAdLimits.PageSize.ShouldBe(20);

    [Fact]
    public void MaxPage_Is100() => PublicJuniorItJobAdLimits.MaxPage.ShouldBe(100);

    [Fact]
    public void MaxMunicipalityLength_HoldsAJobTechConceptId()
    {
        PublicJuniorItJobAdLimits.MaxMunicipalityLength.ShouldBe(32);
        "AvNB_uwa_6n6".Length.ShouldBeLessThanOrEqualTo(PublicJuniorItJobAdLimits.MaxMunicipalityLength);
    }

    [Fact]
    public void Orders_AreNewestFirstAndSoonestDeadlineFirst_AndNothingElse() =>
        PublicJuniorItJobAdLimits.Orders.ShouldBe([JobAdSortBy.PublishedAtDesc, JobAdSortBy.ExpiresAtAsc]);
}
