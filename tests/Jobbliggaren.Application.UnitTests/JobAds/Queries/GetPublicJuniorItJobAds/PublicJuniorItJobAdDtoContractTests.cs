using System.Reflection;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>
/// Pins the junior IT list's answer (ADR 0186). Every public instance property reaches an unauthenticated response, so
/// each set is exhaustive rather than a list of banned names.
/// </summary>
public class PublicJuniorItJobAdDtoContractTests
{
    private static readonly string[] RowProperties =
        ["Id", "Title", "CompanyName", "MunicipalityName", "RegionName", "PublishedAt", "ExpiresAt"];

    private static readonly string[] PlaceProperties = ["MunicipalityConceptId", "Name", "Count"];

    private static string[] PropertiesOf(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void PublicJuniorItJobAdDto_SurfacesExactlyTheseSevenProperties() =>
        PropertiesOf(typeof(PublicJuniorItJobAdDto)).ShouldBe(
            [.. RowProperties.Order(StringComparer.Ordinal)],
            "never the description or an excerpt, the link to Arbetsförmedlingen, a concept id, the org.nr or the raw "
            + "payload.");

    [Fact]
    public void PublicJuniorItPlaceDto_SurfacesExactlyTheseThreeProperties() =>
        PropertiesOf(typeof(PublicJuniorItPlaceDto)).ShouldBe([.. PlaceProperties.Order(StringComparer.Ordinal)]);

    [Fact]
    public void Available_IsThePageTheKommunOptionsAndTheFloorsOutcome_AndNothingElse()
    {
        PropertiesOf(typeof(PublicJuniorItJobAdsPage.Available)).ShouldBe(["FloorHolds", "Places", "Rows"]);

        typeof(PublicJuniorItJobAdsPage.Available).GetProperty(nameof(PublicJuniorItJobAdsPage.Available.Rows))!
            .PropertyType.ShouldBe(typeof(PagedResult<PublicJuniorItJobAdDto>));
        typeof(PublicJuniorItJobAdsPage.Available).GetProperty(nameof(PublicJuniorItJobAdsPage.Available.Places))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<PublicJuniorItPlaceDto>));
        typeof(PublicJuniorItJobAdsPage.Available).GetProperty(nameof(PublicJuniorItJobAdsPage.Available.FloorHolds))!
            .PropertyType.ShouldBe(typeof(bool), "only the floor's outcome reaches the wire, never a count per employer");
    }

    [Fact]
    public void PublicJuniorItJobAdsPage_HasTwoArms_APageAndAPagePastTheEnd() =>
        typeof(PublicJuniorItJobAdsPage).GetNestedTypes()
            .Where(t => t.IsSubclassOf(typeof(PublicJuniorItJobAdsPage)))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Available", "NoSuchPage"]);

    [Fact]
    public void GetPublicJuniorItJobAdsQuery_TakesTheKommunTheOrderAndThePage_AndNothingElse()
    {
        var properties = typeof(GetPublicJuniorItJobAdsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.PropertyType, StringComparer.Ordinal);

        properties.Keys.Order(StringComparer.Ordinal).ShouldBe(["Municipality", "Page", "SortBy"]);
        properties["Municipality"].ShouldBe(typeof(string));
        properties["SortBy"].ShouldBe(typeof(JobAdSortBy));
        properties["Page"].ShouldBe(typeof(int));
    }

    [Fact]
    public void GetPublicJuniorItJobAdsQuery_IsAnsweredForAnyone_AndNeverRecordsASearch()
    {
        typeof(GetPublicJuniorItJobAdsQuery).IsAssignableTo(typeof(IAuthenticatedRequest)).ShouldBeFalse();
        typeof(GetPublicJuniorItJobAdsQuery).IsAssignableTo(typeof(ICapturesRecentSearch)).ShouldBeFalse();
    }
}
