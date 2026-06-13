using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.SavedSearches.Queries;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Queries;

// C2-kontraktsvakthund (architect F5.5): SavedSearch-API:t konsumeras INTE av
// FE (verifierat on-disk, ADR 0039-amendment 2026-05-20) → DTO:n renamesas
// FRITT: Ssyk → OccupationGroup, +Municipality; SsykLabels →
// OccupationGroupLabels, +MunicipalityLabels. Positionsordningen följer den
// kanoniska dimensionsordningen (OccupationGroup, Municipality, Region —
// architect F1; samma SPOT-ordning genom hela kedjan), labels sist (additiv
// konvention från ADR 0043-utökningen).
public class SavedSearchDtoContractTests
{
    [Fact]
    public void SavedSearchDto_ShouldExposeConceptIdProperties_InC2Form()
    {
        var t = typeof(SavedSearchDto);

        t.GetProperty(nameof(SavedSearchDto.Id))!
            .PropertyType.ShouldBe(typeof(Guid));
        t.GetProperty(nameof(SavedSearchDto.Name))!
            .PropertyType.ShouldBe(typeof(string));
        t.GetProperty("OccupationGroup")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("Municipality")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("Region")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 råa listor (inga labels, Fas E).
        t.GetProperty("EmploymentType")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty("WorktimeExtent")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty(nameof(SavedSearchDto.Q))!
            .PropertyType.ShouldBe(typeof(string));
        t.GetProperty(nameof(SavedSearchDto.SortBy))!
            .PropertyType.ShouldBe(typeof(JobAdSortBy));
        t.GetProperty(nameof(SavedSearchDto.NotificationEnabled))!
            .PropertyType.ShouldBe(typeof(bool));
        t.GetProperty(nameof(SavedSearchDto.LastRunAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset?));
        t.GetProperty(nameof(SavedSearchDto.CreatedAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset));
        t.GetProperty(nameof(SavedSearchDto.UpdatedAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset));
    }

    [Fact]
    public void SavedSearchDto_ShouldNotExposeSsykProperties_AfterC2()
    {
        // CTO-dom (e)/(f): occupation-name-dimensionen avvecklas helt ur
        // SavedSearch-kontraktet — ingen FE-konsument finns (F5.5).
        var t = typeof(SavedSearchDto);

        t.GetProperty("Ssyk").ShouldBeNull();
        t.GetProperty("SsykLabels").ShouldBeNull();
    }

    [Fact]
    public void SavedSearchDto_ShouldExposeLabelProjections_PerDimension()
    {
        var t = typeof(SavedSearchDto);

        t.GetProperty("OccupationGroupLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty("MunicipalityLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty("RegionLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
    }

    [Fact]
    public void SavedSearchDto_ShouldKeepCanonicalPositionalOrder()
    {
        // Kanonisk dimensionsordning (architect F1) i råfälten; labels sist
        // (additiv konvention). Named arguments krävs ändå vid konstruktion —
        // detta test gör ordningen granskningsbar.
        var ctor = typeof(SavedSearchDto)
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var names = ctor.GetParameters().Select(p => p.Name).ToArray();

        // ADR 0067 Beslut 6 (Fas B2): EmploymentType/WorktimeExtent infogade
        // efter Region (kanonisk dimensionsordning), labels fortsatt sist.
        names.Length.ShouldBe(16);
        names[..13].ShouldBe(
        [
            "Id", "Name", "OccupationGroup", "Municipality", "Region",
            "EmploymentType", "WorktimeExtent", "Q",
            "SortBy", "NotificationEnabled", "LastRunAt", "CreatedAt", "UpdatedAt",
        ]);
        names[13..].ShouldBe(
        [
            "OccupationGroupLabels", "MunicipalityLabels", "RegionLabels",
        ]);
    }
}
