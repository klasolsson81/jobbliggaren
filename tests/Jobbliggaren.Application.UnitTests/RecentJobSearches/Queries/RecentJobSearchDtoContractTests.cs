using System.Reflection;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.RecentJobSearches.Queries;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Queries;

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
        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 råa listor (inga labels, Fas E).
        t.GetProperty(nameof(RecentJobSearchDto.EmploymentTypeList))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        t.GetProperty(nameof(RecentJobSearchDto.WorktimeExtentList))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<string>));
        // #1407 — distans-axeln (skalär, ingen label-dimension).
        t.GetProperty(nameof(RecentJobSearchDto.Remote))!
            .PropertyType.ShouldBe(typeof(bool));
        t.GetProperty(nameof(RecentJobSearchDto.OccupationGroupLabels))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty(nameof(RecentJobSearchDto.MunicipalityLabels))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty(nameof(RecentJobSearchDto.RegionLabels))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<TaxonomyLabelDto>));
        t.GetProperty(nameof(RecentJobSearchDto.SortBy))!
            .PropertyType.ShouldBe(typeof(JobAdSortBy));
        // #1430 — labeln är STRUKTUR, inte prosa: en färdig sträng kunde bara vara på ett
        // språk, och den nådde en engelsk användare ordagrant på tre ytor.
        t.GetProperty(nameof(RecentJobSearchDto.Label))!
            .PropertyType.ShouldBe(typeof(RecentSearchLabelDto));
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

    // #1407 (security-auditor M-1). ShouldKeepCanonicalPositionalOrder läser ctor.GetParameters()
    // och ser därför BARA positionella parametrar. En body-deklarerad
    // `public string[] Whatever { get; init; }` är osynlig för den och serialiseras
    // ändå till wire:n av System.Text.Json — mätt. Namnbaserad frånvaro-assertion
    // stänger inte det hålet heller: den matchar stavningar, inte formen.
    //
    // Mängden är därför uttömmande och EXAKT. Vilken ny property som helst, oavsett
    // namn och deklarationsform, faller ut här. Vilka dimensioner som FÅR vara med
    // och varför en utelämnas ägs av RecentJobSearchProjectionParityTests.
    private static readonly string[] SurfacedProperties =
    [
        "Id", "Q",
        "OccupationGroupList", "MunicipalityList", "RegionList",
        "EmploymentTypeList", "WorktimeExtentList", "Remote",
        "OccupationGroupLabels", "MunicipalityLabels", "RegionLabels",
        "SortBy", "Label", "CurrentCount", "NewCount", "LastViewedAt",
    ];

    [Fact]
    public void RecentJobSearchDto_ShouldSurfaceExactlyTheseProperties()
    {
        var actual = typeof(RecentJobSearchDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        actual.ShouldBe(
            [.. SurfacedProperties.OrderBy(n => n, StringComparer.Ordinal)],
            "varje public instans-property på RecentJobSearchDto når HTTP-svaret. "
            + "Är den nya propertyn en SÖKDIMENSION — en axel filtret bär — klassa den "
            + "först i RecentJobSearchProjectionParityTests. Är den presentation eller "
            + "räknare (Label, CurrentCount, NewCount) eller ett bokföringsfält som "
            + "redan står i den filens NotSearchDimensions (Id, LastViewedAt), är den "
            + "här listan hela grinden.");
    }

    [Fact]
    public void RecentJobSearchDto_ShouldKeepCanonicalPositionalOrder()
    {
        var ctor = typeof(RecentJobSearchDto)
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var names = ctor.GetParameters().Select(p => p.Name).ToArray();

        // ADR 0067 Beslut 6 (Fas B2): EmploymentTypeList/WorktimeExtentList efter
        // RegionList (kanonisk dimensionsordning), labels-blocket fortsatt sist.
        // #1407: Remote sist i råa-dimensions-blocket — samma position den har
        // RELATIVT de råa dimensionslistorna i JobAdFilterCriteria (där Q ligger
        // sist och här på index 1; de två ordningarna divergerade före #1407).
        names.ShouldBe(SurfacedProperties);
    }
}
