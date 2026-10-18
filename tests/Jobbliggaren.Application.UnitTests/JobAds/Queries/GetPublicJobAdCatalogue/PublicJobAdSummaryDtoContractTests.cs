using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// Pins the catalogue row's exact member set (ADR 0175 F5). Every public instance property reaches an unauthenticated
/// response that lists the whole corpus, so the set is exhaustive rather than a list of banned names.
/// </summary>
public class PublicJobAdSummaryDtoContractTests
{
    private static readonly string[] SurfacedProperties =
        ["Id", "Title", "CompanyName", "MunicipalityName", "RegionName", "PublishedAt"];

    [Fact]
    public void PublicJobAdSummaryDto_SurfacesExactlyTheseProperties()
    {
        var actual = typeof(PublicJobAdSummaryDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        actual.ShouldBe(
            [.. SurfacedProperties.OrderBy(n => n, StringComparer.Ordinal)],
            "this row lists every listed ad to anonymous readers and crawlers. Never the description or an excerpt, "
            + "the link to Arbetsförmedlingen, the deadline, a concept id, the org.nr or the raw payload; any addition "
            + "fires ADR 0166 D10 trigger 10 and is a decision for that ADR, not for this list.");
    }

    [Fact]
    public void GetPublicJobAdCatalogueQuery_IsAnsweredForAnyone_AndNeverRecordsASearch()
    {
        // The union exists for its 404 and 503 arms. What keeps an anonymous read from capturing a search is that the
        // message is not ICapturesRecentSearch (the capture behaviour's first gate), not the response type.
        typeof(GetPublicJobAdCatalogueQuery).IsAssignableTo(typeof(IAuthenticatedRequest)).ShouldBeFalse();
        typeof(GetPublicJobAdCatalogueQuery).IsAssignableTo(typeof(ICapturesRecentSearch)).ShouldBeFalse();
    }
}
