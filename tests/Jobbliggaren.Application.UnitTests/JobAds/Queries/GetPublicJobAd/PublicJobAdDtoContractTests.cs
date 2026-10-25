using System.Reflection;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// Pins the anonymous DTO's exact member set (ADR 0160). Every public instance property reaches an
/// unauthenticated response, so the set is exhaustive rather than a list of banned names: a banned
/// name can be spelt another way, an unlisted property cannot hide here.
/// </summary>
public class PublicJobAdDtoContractTests
{
    private static readonly string[] SurfacedProperties =
    [
        "Id", "Title", "CompanyName", "Description", "Url", "ApplicationUrl", "PublishedAt", "ExpiresAt",
        "MunicipalityConceptId", "MunicipalityName", "RegionConceptId", "RegionName",
        "OccupationGroupConceptId", "OccupationGroupName", "EmploymentTypeConceptId", "WorktimeExtentConceptId",
    ];

    [Fact]
    public void PublicJobAdDto_SurfacesExactlyTheseProperties()
    {
        var actual = typeof(PublicJobAdDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        actual.ShouldBe(
            [.. SurfacedProperties.OrderBy(n => n, StringComparer.Ordinal)],
            "this record is served to anonymous readers and crawlers. Never add user state, match "
            + "data, the contact block, the org.nr, the raw payload or the extracted terms (ADR 0160); "
            + "any other addition is a decision for that ADR, not for this list.");
    }
}
