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
    public void RecentJobSearchDto_ShouldNotSurfaceTheEmployerAxis()
    {
        // ADR 0087 D8(c). Sedan #1407 bär DTO:n Remote men INTE Employer, trots att
        // handlern trådar in båda i CountAsync — en asymmetri som ser ut som en
        // inkonsekvens att städa bort. Den är det inte: för en enskild firma ÄR
        // org.nr innehavarens personnummer (#841), så axeln får inte nå wire:n ens
        // till priset av att en arbetsgivarsökning inte kan köras igen. Utan denna
        // pinne kostar "gör det konsekvent" en PII-läcka.
        var t = typeof(RecentJobSearchDto);

        t.GetProperty("Employer").ShouldBeNull();
        t.GetProperty("EmployerList").ShouldBeNull();
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
        // #1407: Remote sist i råa-dimensions-blocket — samma plats den har i
        // JobAdFilterCriteria, minus den Employer DTO:n aldrig bär (nedan).
        names.ShouldBe(
        [
            "Id", "Q",
            "OccupationGroupList", "MunicipalityList", "RegionList",
            "EmploymentTypeList", "WorktimeExtentList", "Remote",
            "OccupationGroupLabels", "MunicipalityLabels", "RegionLabels",
            "SortBy", "Label", "CurrentCount", "NewCount", "LastViewedAt",
        ]);
    }
}
