using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Application.RecentJobSearches.Queries;
using JobbPilot.Domain.JobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.RecentJobSearches.Queries;

// C2-kontraktsvakthund (architect F5 — villkorat Blocker, löst med ADDITIV
// DTO). FE-zod-schemat (web/.../recent-searches.ts) har `ssykList` REQUIRED —
// rakt rename Ssyk*→OccupationGroup* skulle bryta /sokningar + /jobb-hero-chip
// + /oversikt. Därför:
//
//   - Befintliga fält + deras inbördes positionsordning OFÖRÄNDRADE
//     (Id, Q, SsykList, RegionList, SsykLabels, RegionLabels, SortBy, Label,
//     CurrentCount, NewCount, LastViewedAt).
//   - SsykList/SsykLabels är deprecated och matas ALLTID med [] (verifieras i
//     ListRecentSearchesQueryHandlerTests); tas bort i Fas E med zod-schemat.
//   - Nya fält tillkommer SIST (samma additiva konvention som
//     SavedSearchDtoContractTests etablerade — zod stripper okända nycklar →
//     osynliga för FE tills Fas E): OccupationGroupList, MunicipalityList,
//     OccupationGroupLabels, MunicipalityLabels.
//
// VAL DOKUMENTERAT (test-writer C2): architect F5 specade inte exakta
// positioner — "nya fält sist" väljs för paritet med SavedSearchDto-
// konventionen och minsta positionella kontraktsyta.
public class RecentJobSearchDtoContractTests
{
    [Fact]
    public void RecentJobSearchDto_ShouldKeepExistingProperties_Unchanged()
    {
        var t = typeof(RecentJobSearchDto);

        t.GetProperty(nameof(RecentJobSearchDto.Id))!
            .PropertyType.ShouldBe(typeof(Guid));
        t.GetProperty(nameof(RecentJobSearchDto.Q))!
            .PropertyType.ShouldBe(typeof(string));
        t.GetProperty("SsykList")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("RegionList")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("SsykLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty("RegionLabels")!
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
    public void RecentJobSearchDto_ShouldExposeAdditiveOccupationGroupAndMunicipalityFields()
    {
        var t = typeof(RecentJobSearchDto);

        t.GetProperty("OccupationGroupList")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("MunicipalityList")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("OccupationGroupLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty("MunicipalityLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
    }

    [Fact]
    public void RecentJobSearchDto_ShouldKeepExistingPositionalOrder_NewFieldsLast()
    {
        // FE-wire-kontraktet är namnbaserat (zod), men positionsordningen låses
        // ändå: befintliga 11 fält först (oförändrad inbördes ordning), nya
        // fält tillkommer SIST — additivitet är granskningsbar i ett test.
        var ctor = typeof(RecentJobSearchDto)
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var names = ctor.GetParameters().Select(p => p.Name).ToArray();

        names.Length.ShouldBe(15);
        names[..11].ShouldBe(
        [
            "Id", "Q", "SsykList", "RegionList", "SsykLabels", "RegionLabels",
            "SortBy", "Label", "CurrentCount", "NewCount", "LastViewedAt",
        ]);
        names[11..].ShouldBe(
        [
            "OccupationGroupList", "MunicipalityList",
            "OccupationGroupLabels", "MunicipalityLabels",
        ]);
    }
}
