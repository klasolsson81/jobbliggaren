using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetRelatedPublicJobAds;

/// <summary>
/// Pins the related answer's shape (ADR 0182): an envelope of exactly one member whose rows are the catalogue's
/// six-member row, the same type and never a copy, and a query whose only input is the anchor's id.
/// </summary>
public class RelatedPublicJobAdsDtoContractTests
{
    [Fact]
    public void RelatedPublicJobAdsDto_HasExactlyOneMember_TheCataloguesRowList()
    {
        var properties = typeof(RelatedPublicJobAdsDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var items = properties.ShouldHaveSingleItem();
        items.Name.ShouldBe("Items");
        items.PropertyType.ShouldBe(typeof(IReadOnlyList<PublicJobAdSummaryDto>),
            "a related row is the catalogue's row (ADR 0176 D5); a row type of its own could grow a member the catalogue "
            + "never had, which ADR 0166 D10 trigger 15 forbids.");
    }

    [Fact]
    public void GetRelatedPublicJobAdsQuery_TakesTheAnchorsIdAndNothingElse()
    {
        var properties = typeof(GetRelatedPublicJobAdsQuery).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var id = properties.ShouldHaveSingleItem();
        id.Name.ShouldBe("Id");
        id.PropertyType.ShouldBe(typeof(Guid),
            "every selection key is read from the anchor's row on the server; a key a caller can supply is ADR 0166 D10 "
            + "trigger 17.");
    }

    [Fact]
    public void GetRelatedPublicJobAdsQuery_IsAnsweredForAnyone_AndNeverRecordsASearch()
    {
        typeof(GetRelatedPublicJobAdsQuery).IsAssignableTo(typeof(IAuthenticatedRequest)).ShouldBeFalse();
        typeof(GetRelatedPublicJobAdsQuery).IsAssignableTo(typeof(ICapturesRecentSearch)).ShouldBeFalse();
    }
}
