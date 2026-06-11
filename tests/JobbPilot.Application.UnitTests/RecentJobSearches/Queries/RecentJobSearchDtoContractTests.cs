using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Application.RecentJobSearches.Queries;
using JobbPilot.Domain.JobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.RecentJobSearches.Queries;

// Kontraktsvakthund. C2 (architect F5) höll DTO:n ADDITIV med deprecated
// alltid-tomma SsykList/SsykLabels eftersom FE-zod krävde `ssykList`.
// E2a frikopplade FE-zod (occupationGroupList); E2b utförde F5-planens
// borttagning (CTO-direktiv commit 3, 2026-06-11) — shimmet är BORTA och
// formen är den slutgiltiga: dimensioner yrkesgrupp → kommun → region,
// labels i samma ordning, sedan SortBy/Label/counters/LastViewedAt.
// Wire-kontraktet är namnbaserat (camelCase, zod) — positionslåset här är
// intern granskningsbarhet, inte wire-yta.
public class RecentJobSearchDtoContractTests
{
    [Fact]
    public void RecentJobSearchDto_ShouldExposeExpectedPropertyTypes()
    {
        var t = typeof(RecentJobSearchDto);

        t.GetProperty(nameof(RecentJobSearchDto.Id))!
            .PropertyType.ShouldBe(typeof(Guid));
        t.GetProperty(nameof(RecentJobSearchDto.Q))!
            .PropertyType.ShouldBe(typeof(string));
        t.GetProperty(nameof(RecentJobSearchDto.OccupationGroupList))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty(nameof(RecentJobSearchDto.MunicipalityList))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty(nameof(RecentJobSearchDto.RegionList))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty(nameof(RecentJobSearchDto.OccupationGroupLabels))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty(nameof(RecentJobSearchDto.MunicipalityLabels))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty(nameof(RecentJobSearchDto.RegionLabels))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty(nameof(RecentJobSearchDto.SortBy))!
            .PropertyType.ShouldBe(typeof(JobAdSortBy));
        t.GetProperty(nameof(RecentJobSearchDto.Label))!
            .PropertyType.ShouldBe(typeof(string));
        t.GetProperty(nameof(RecentJobSearchDto.CurrentCount))!
            .PropertyType.ShouldBe(typeof(int));
        t.GetProperty(nameof(RecentJobSearchDto.NewCount))!
            .PropertyType.ShouldBe(typeof(int));
        t.GetProperty(nameof(RecentJobSearchDto.LastViewedAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset));
    }

    [Fact]
    public void RecentJobSearchDto_ShouldNotCarryDeprecatedSsykShim()
    {
        // E2b-vakthund: C2-shimmet får inte återuppstå — occupation-name-
        // dimensionen finns inte i sök-identiteten (C2 CTO-dom (e)).
        var t = typeof(RecentJobSearchDto);

        t.GetProperty("SsykList").ShouldBeNull();
        t.GetProperty("SsykLabels").ShouldBeNull();
    }

    [Fact]
    public void RecentJobSearchDto_ShouldKeepCanonicalPositionalOrder()
    {
        var ctor = typeof(RecentJobSearchDto)
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var names = ctor.GetParameters().Select(p => p.Name).ToArray();

        names.ShouldBe(
        [
            "Id", "Q",
            "OccupationGroupList", "MunicipalityList", "RegionList",
            "OccupationGroupLabels", "MunicipalityLabels", "RegionLabels",
            "SortBy", "Label", "CurrentCount", "NewCount", "LastViewedAt",
        ]);
    }
}
