using System.Reflection;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Application.SavedSearches.Queries;
using JobbPilot.Domain.JobAds;
using Shouldly;

namespace JobbPilot.Application.UnitTests.SavedSearches.Queries;

// ADR 0039/0043-kontraktsvakthund (CTO 2026-05-17). SavedSearchDto-utökningen
// måste vara ADDITIV: befintliga concept-id-fält + ordning OFÖRÄNDRADE (FE-DTO
// + ADR 0039-jsonb-konsumenter bryts inte), nya label-fält tillkommer.
// SsykLabels/RegionLabels speglar ITaxonomyReadModel.ResolveLabelsAsync-
// kontraktet (IReadOnlyList<TaxonomyLabelDto>).
public class SavedSearchDtoContractTests
{
    [Fact]
    public void SavedSearchDto_ShouldKeepExistingConceptIdProperties_Unchanged()
    {
        var t = typeof(SavedSearchDto);

        t.GetProperty(nameof(SavedSearchDto.Id))!
            .PropertyType.ShouldBe(typeof(Guid));
        t.GetProperty(nameof(SavedSearchDto.Name))!
            .PropertyType.ShouldBe(typeof(string));
        t.GetProperty(nameof(SavedSearchDto.Ssyk))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty(nameof(SavedSearchDto.Region))!
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
    public void SavedSearchDto_ShouldExposeAdditiveLabelProjections()
    {
        var t = typeof(SavedSearchDto);

        t.GetProperty("SsykLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty("RegionLabels")!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
    }

    [Fact]
    public void SavedSearchDto_ShouldKeepRawConceptIdPositionalOrder_ForBackCompat()
    {
        // ADR 0039: jsonb/VO-projektionens positionella kontrakt (de första
        // 10 fälten) får inte permuteras. Nya label-fält ska tillkomma SIST
        // (additivt) — annars bryts positionella konsumenter/serialisering.
        var ctor = typeof(SavedSearchDto)
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var names = ctor.GetParameters().Select(p => p.Name).ToArray();

        names.Length.ShouldBeGreaterThanOrEqualTo(12);
        names[..10].ShouldBe(
        [
            "Id", "Name", "Ssyk", "Region", "Q", "SortBy",
            "NotificationEnabled", "LastRunAt", "CreatedAt", "UpdatedAt",
        ]);
        names.ShouldContain("SsykLabels");
        names.ShouldContain("RegionLabels");
    }
}
