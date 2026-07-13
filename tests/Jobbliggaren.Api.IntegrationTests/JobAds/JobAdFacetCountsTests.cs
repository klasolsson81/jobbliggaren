using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0067 Beslut 4 (Platsbanken sök-paritet Fas D1) — per-option facet-counts.
// Verifierar IJobAdSearchQuery.FacetCountsAsync mot riktig Postgres
// (Testcontainers, ALDRIG EF-InMemory: GROUP BY mot facett-kolumn +
// EF.Property<string?>-translation översätts enbart av Npgsql — InMemory ger
// falska gröna, jfr feedback_ef_strongly_typed_vo_contains_translation).
//
// ADR 0062 — sök-kompositionen bor i Infrastructure-impl:en JobAdSearchQuery
// (internal, InternalsVisibleTo Api.IntegrationTests). Instansieras direkt mot
// Testcontainers-Postgres (speglar ListJobAdsMultiFilterTests) — synonymExpander
// är irrelevant för facett-vägen (ingen q) men kontraktet kräver argumentet.
//
// KÄRNAN (ADR 0067 Beslut 4 facett-exkluderings-semantik): counten för dimension
// X reflekterar alla ANDRA aktiva filter i criteria men INTE X självt (annars fel
// siffror vs Platsbanken). NULL-shadow exkluderas → ingen null-nyckel.
//
// E2c (CTO VAL 4, 2026-06-11): ort är EN dimension i två granulariteter
// (geo-union i ApplyCriteria sedan E2b) — ort-facetterna (Municipality/Region)
// exkluderar HELA ort-dimensionen (båda listorna) ur WHERE. Se VAL 4-facts
// längst ned.
[Collection("Api")]
public class JobAdFacetCountsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Seedar en importerad, AKTIV JobAd med valfria shadow-värden via raw_payload.
    // occupation_group är TOP-LEVEL; municipality + region bor BÅDA i
    // workplace_address (verifierat mot JobAdFacetsSurvivePurgeTests + JobAdConfiguration).
    private async Task SeedAsync(
        string title,
        string? occupationGroup,
        string? municipality,
        string? region,
        CancellationToken ct,
        bool archived = false,
        string? employmentType = null,
        string? worktimeExtent = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";

        var occupationGroupJson = occupationGroup is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroup}\"}}";

        // workplace_address bär BÅDE region_concept_id och municipality_concept_id.
        var addressFields = new List<string>();
        if (region is not null)
            addressFields.Add($"\"region_concept_id\":\"{region}\"");
        if (municipality is not null)
            addressFields.Add($"\"municipality_concept_id\":\"{municipality}\"");
        var workplaceAddressJson = addressFields.Count == 0
            ? "null"
            : "{" + string.Join(",", addressFields) + "}";

        // B2 (ADR 0067 Beslut 6/7): Klass 2-nycklar är TOP-LEVEL. worktime via
        // wire-key "working_hours_type" → worktime_extent_concept_id (namnglapp
        // medvetet).
        var employmentTypeJson = employmentType is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentType}\"}}";
        var worktimeExtentJson = worktimeExtent is null
            ? "null"
            : $"{{\"concept_id\":\"{worktimeExtent}\"}}";

        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{occupationGroupJson}," +
            $"\"employment_type\":{employmentTypeJson}," +
            $"\"working_hours_type\":{worktimeExtentJson}," +
            $"\"workplace_address\":{workplaceAddressJson}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        if (archived)
            jobAd.Archive(clock).IsSuccess.ShouldBeTrue();

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static JobAdSearchQuery CreateSut(IServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Substitute.For<IOccupationSynonymExpander>());

    private static JobAdFilterCriteria Criteria(
        IReadOnlyList<string>? occupationGroup = null,
        IReadOnlyList<string>? municipality = null,
        IReadOnlyList<string>? region = null,
        string? q = null,
        IReadOnlyList<string>? employmentType = null,
        IReadOnlyList<string>? worktimeExtent = null) =>
        new(
            OccupationGroup: occupationGroup ?? [],
            Municipality: municipality ?? [],
            Region: region ?? [],
            EmploymentType: employmentType ?? [],
            WorktimeExtent: worktimeExtent ?? [],
            Employer: [],
            Q: q);

    [Fact]
    public async Task FacetCountsAsync_ShouldExcludeFacetedDimensionFilter_WhenCountingOccupationGroup()
    {
        // Exkludering: trots att criteria filtrerar OccupationGroup=[grpA] ska
        // counten räkna ALLA yrkesgrupper (grpA, grpB, grpC) — X-filtret tas bort
        // för X:s egen facett (ADR 0067 Beslut 4). Annars vore varje annan grupp 0.
        var ct = TestContext.Current.CancellationToken;
        var grpA = $"grp{Guid.NewGuid():N}"[..16];
        var grpB = $"grp{Guid.NewGuid():N}"[..16];
        var grpC = $"grp{Guid.NewGuid():N}"[..16];

        await SeedAsync("A1", grpA, null, null, ct);
        await SeedAsync("A2", grpA, null, null, ct);
        await SeedAsync("B1", grpB, null, null, ct);
        await SeedAsync("C1", grpC, null, null, ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(occupationGroup: [grpA]), FacetDimension.OccupationGroup, ct);

        // Alla tre grupper finns med trots OccupationGroup=[grpA]-filtret.
        counts[grpA].ShouldBe(2);
        counts[grpB].ShouldBe(1);
        counts[grpC].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldReflectOtherActiveFilters_WhenCountingOccupationGroup()
    {
        // Andra-filter reflekteras: facett på OccupationGroup MEN med Region=[regX]
        // aktivt → bara annonser i regX räknas per yrkesgrupp (det andra filtret
        // består; endast den facetterade dimensionen exkluderas).
        var ct = TestContext.Current.CancellationToken;
        var grpA = $"grp{Guid.NewGuid():N}"[..16];
        var grpB = $"grp{Guid.NewGuid():N}"[..16];
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var regY = $"reg{Guid.NewGuid():N}"[..16];

        await SeedAsync("X-A", grpA, null, regX, ct);   // räknas (regX)
        await SeedAsync("X-B", grpB, null, regX, ct);   // räknas (regX)
        await SeedAsync("Y-A", grpA, null, regY, ct);   // EXKLUDERAS (regY)

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(region: [regX]), FacetDimension.OccupationGroup, ct);

        // grpA räknas bara i regX (1, inte 2) — region-filtret reflekteras.
        counts[grpA].ShouldBe(1);
        counts[grpB].ShouldBe(1);
        counts.ShouldNotContainKey(regY); // sanity: bara grupp-nycklar
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldExcludeNullKey_WhenSomeJobAdsLackTheDimension()
    {
        // NULL-nyckel exkluderas: annons utan OccupationGroup ska inte ge en
        // null/tom nyckel i dictionaryn (EF.Property<string?> != null-predikatet).
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];

        await SeedAsync("HasGroup", grp, null, null, ct);
        await SeedAsync("NoGroup", null, null, null, ct);   // NULL OccupationGroup

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.OccupationGroup, ct);

        counts[grp].ShouldBe(1);
        counts.Keys.ShouldNotContain(k => string.IsNullOrEmpty(k));
        counts.ShouldNotContainKey(string.Empty);
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldExcludeArchivedJobAds_WhenCounting()
    {
        // Status=Active-disciplin ärvs (ApplyCriteria-SPOT, ADR 0032-amendment):
        // arkiverad annons räknas ALDRIG i facetten.
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];

        await SeedAsync("Active", grp, null, null, ct);
        await SeedAsync("Archived", grp, null, null, ct, archived: true);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.OccupationGroup, ct);

        // Endast den aktiva annonsen räknas (1, ej 2).
        counts[grp].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldCountPerMunicipality_WhenDimensionIsMunicipality()
    {
        // Municipality-dimensionen (workplace_address.municipality_concept_id).
        var ct = TestContext.Current.CancellationToken;
        var muniA = $"kn{Guid.NewGuid():N}"[..16];
        var muniB = $"kn{Guid.NewGuid():N}"[..16];

        await SeedAsync("M-A1", null, muniA, null, ct);
        await SeedAsync("M-A2", null, muniA, null, ct);
        await SeedAsync("M-B1", null, muniB, null, ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.Municipality, ct);

        counts[muniA].ShouldBe(2);
        counts[muniB].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldCountPerRegion_WhenDimensionIsRegion()
    {
        // Region-dimensionen (workplace_address.region_concept_id).
        var ct = TestContext.Current.CancellationToken;
        var regA = $"reg{Guid.NewGuid():N}"[..16];
        var regB = $"reg{Guid.NewGuid():N}"[..16];

        await SeedAsync("R-A1", null, null, regA, ct);
        await SeedAsync("R-B1", null, null, regB, ct);
        await SeedAsync("R-B2", null, null, regB, ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.Region, ct);

        counts[regA].ShouldBe(1);
        counts[regB].ShouldBe(2);
    }

    [Fact]
    public async Task FacetCountsAsync_MunicipalityFacet_ExcludesWholeOrtDimension_RegionFilterIgnored()
    {
        // CTO VAL 4: Municipality-facetten exkluderar HELA ort-dimensionen —
        // ett aktivt Region-filter får INTE begränsa kommun-countsen (kommuner
        // i andra län räknas också; annars vore "Solna (12)" fel mot unionen).
        var ct = TestContext.Current.CancellationToken;
        var muniInX = $"kn{Guid.NewGuid():N}"[..16];
        var muniInY = $"kn{Guid.NewGuid():N}"[..16];
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var regY = $"reg{Guid.NewGuid():N}"[..16];

        await SeedAsync("IX", null, muniInX, regX, ct);
        await SeedAsync("IY", null, muniInY, regY, ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(region: [regX]), FacetDimension.Municipality, ct);

        // Båda kommunerna räknas — region-filtret är exkluderat (ort-dimension).
        counts[muniInX].ShouldBe(1);
        counts[muniInY].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_RegionFacet_ExcludesWholeOrtDimension_MunicipalityFilterIgnored()
    {
        // Spegelbild: Region-facetten exkluderar även Municipality-listan.
        var ct = TestContext.Current.CancellationToken;
        var muniA = $"kn{Guid.NewGuid():N}"[..16];
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var regY = $"reg{Guid.NewGuid():N}"[..16];

        await SeedAsync("RX", null, muniA, regX, ct);
        await SeedAsync("RY", null, null, regY, ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(municipality: [muniA]), FacetDimension.Region, ct);

        counts[regX].ShouldBe(1);
        counts[regY].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_MunicipalityFacet_WithBothOrtListsActive_ExcludesBoth()
    {
        // Båda ort-listorna satta → båda exkluderade ur Municipality-facetten;
        // andra-dimensions-filter (yrke) reflekteras fortfarande.
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        var muniA = $"kn{Guid.NewGuid():N}"[..16];
        var muniB = $"kn{Guid.NewGuid():N}"[..16];
        var regX = $"reg{Guid.NewGuid():N}"[..16];

        await SeedAsync("GA", grp, muniA, regX, ct);
        await SeedAsync("GB", grp, muniB, null, ct);
        await SeedAsync("FelYrke", $"grp{Guid.NewGuid():N}"[..16], muniB, null, ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(occupationGroup: [grp], municipality: [muniA], region: [regX]),
            FacetDimension.Municipality, ct);

        // muniB räknas trots att varken muniB eller dess (saknade) region är
        // valda — ort-exkludering total; FelYrke räknas EJ (yrke-filtret kvar).
        counts[muniA].ShouldBe(1);
        counts[muniB].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_OccupationGroupFacet_InheritsGeoUnionViaSpot()
    {
        // Regressionsvakt (E2b geo-union ärvd via ApplyCriteria-SPOT): yrke-
        // facetten med BÅDA ort-listorna aktiva räknar annonser som matchar
        // region-grenen ELLER kommun-grenen — inte AND-snittet.
        var ct = TestContext.Current.CancellationToken;
        var grpA = $"grp{Guid.NewGuid():N}"[..16];
        var grpB = $"grp{Guid.NewGuid():N}"[..16];
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var muniInY = $"kn{Guid.NewGuid():N}"[..16];

        await SeedAsync("ViaRegion", grpA, null, regX, ct);
        await SeedAsync("ViaKommun", grpB, muniInY, $"reg{Guid.NewGuid():N}"[..16], ct);
        await SeedAsync("UtanförOrt", grpA, $"kn{Guid.NewGuid():N}"[..16],
            $"reg{Guid.NewGuid():N}"[..16], ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(municipality: [muniInY], region: [regX]),
            FacetDimension.OccupationGroup, ct);

        counts[grpA].ShouldBe(1); // ViaRegion (UtanförOrt exkluderad av unionen)
        counts[grpB].ShouldBe(1); // ViaKommun
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldReturnEmptyDictionary_WhenNoActiveJobAdsMatch()
    {
        // Tom korpus / ingen match → tom dictionary. Filtrera på ett region-id
        // som inte seedats (delad [Collection("Api")]-korpus → använd ett unikt id
        // som ingen annan annons bär, så facetten över OccupationGroup blir tom).
        var ct = TestContext.Current.CancellationToken;
        var unusedRegion = $"reg{Guid.NewGuid():N}"[..16];

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(region: [unusedRegion]), FacetDimension.OccupationGroup, ct);

        counts.ShouldBeEmpty();
    }

    // ===============================================================
    // B2 (ADR 0067 Beslut 6/7) — EmploymentType + WorktimeExtent-facetter.
    // Till skillnad från ort (geo-granulariteter) är dessa två ORTOGONALA:
    // EmploymentType-facetten exkluderar BARA sin egen lista (inte worktime).
    // ===============================================================

    [Fact]
    public async Task FacetCountsAsync_ShouldCountPerEmploymentType_WhenDimensionIsEmploymentType()
    {
        var ct = TestContext.Current.CancellationToken;
        var etA = $"et{Guid.NewGuid():N}"[..16];
        var etB = $"et{Guid.NewGuid():N}"[..16];

        await SeedAsync("E-A1", null, null, null, ct, employmentType: etA);
        await SeedAsync("E-A2", null, null, null, ct, employmentType: etA);
        await SeedAsync("E-B1", null, null, null, ct, employmentType: etB);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.EmploymentType, ct);

        counts[etA].ShouldBe(2);
        counts[etB].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_ShouldCountPerWorktimeExtent_WhenDimensionIsWorktimeExtent()
    {
        var ct = TestContext.Current.CancellationToken;
        var wtA = $"wt{Guid.NewGuid():N}"[..16];
        var wtB = $"wt{Guid.NewGuid():N}"[..16];

        await SeedAsync("W-A1", null, null, null, ct, worktimeExtent: wtA);
        await SeedAsync("W-B1", null, null, null, ct, worktimeExtent: wtB);
        await SeedAsync("W-B2", null, null, null, ct, worktimeExtent: wtB);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.WorktimeExtent, ct);

        counts[wtA].ShouldBe(1);
        counts[wtB].ShouldBe(2);
    }

    [Fact]
    public async Task FacetCountsAsync_EmploymentTypeFacet_ExcludesOnlyOwnList_NotWorktime()
    {
        // KÄRNAN (B2, ortogonalitet): EmploymentType-facetten exkluderar sin EGEN
        // lista (annars vore alla andra anställningsformer 0) MEN — till skillnad
        // från ort — exkluderar INTE worktime. Ett aktivt WorktimeExtent-filter
        // ska fortfarande begränsa employment-countsen.
        var ct = TestContext.Current.CancellationToken;
        var etA = $"et{Guid.NewGuid():N}"[..16];
        var etB = $"et{Guid.NewGuid():N}"[..16];
        var wtHel = $"wt{Guid.NewGuid():N}"[..16];
        var wtDel = $"wt{Guid.NewGuid():N}"[..16];

        // etA finns med heltid OCH deltid; etB endast deltid.
        await SeedAsync("AHel", null, null, null, ct, employmentType: etA, worktimeExtent: wtHel);
        await SeedAsync("ADel", null, null, null, ct, employmentType: etA, worktimeExtent: wtDel);
        await SeedAsync("BDel", null, null, null, ct, employmentType: etB, worktimeExtent: wtDel);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        // Facett över EmploymentType MED ett EmploymentType-filter (egen lista →
        // exkluderas) OCH ett WorktimeExtent=heltid-filter (annan dimension →
        // består).
        var counts = await sut.FacetCountsAsync(
            Criteria(employmentType: [etA], worktimeExtent: [wtHel]),
            FacetDimension.EmploymentType, ct);

        // Endast heltidsannonser räknas (worktime-filtret består): etA=1 (AHel),
        // etB saknas helt (bara deltid). Egen etA-lista exkluderad → etA ändå med.
        counts[etA].ShouldBe(1);
        counts.ShouldNotContainKey(etB);
    }

    [Fact]
    public async Task FacetCountsAsync_WorktimeExtentFacet_ExcludesOnlyOwnList_NotEmployment()
    {
        // Spegelbild: WorktimeExtent-facetten exkluderar sin egen lista men
        // INTE employment — ett aktivt EmploymentType-filter består.
        var ct = TestContext.Current.CancellationToken;
        var etMatch = $"et{Guid.NewGuid():N}"[..16];
        var etOther = $"et{Guid.NewGuid():N}"[..16];
        var wtA = $"wt{Guid.NewGuid():N}"[..16];
        var wtB = $"wt{Guid.NewGuid():N}"[..16];

        await SeedAsync("MA", null, null, null, ct, employmentType: etMatch, worktimeExtent: wtA);
        await SeedAsync("MB", null, null, null, ct, employmentType: etMatch, worktimeExtent: wtB);
        await SeedAsync("OtherA", null, null, null, ct, employmentType: etOther, worktimeExtent: wtA);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(employmentType: [etMatch], worktimeExtent: [wtA]),
            FacetDimension.WorktimeExtent, ct);

        // Bara etMatch-annonser räknas (employment-filtret består); egen
        // worktime-lista exkluderad → både wtA och wtB med för etMatch.
        counts[wtA].ShouldBe(1);
        counts[wtB].ShouldBe(1);
    }

    [Fact]
    public async Task FacetCountsAsync_EmploymentTypeFacet_ExcludesNullKey_WhenSomeJobAdsLackTheDimension()
    {
        var ct = TestContext.Current.CancellationToken;
        var et = $"et{Guid.NewGuid():N}"[..16];

        await SeedAsync("HasEt", null, null, null, ct, employmentType: et);
        await SeedAsync("NoEt", null, null, null, ct, employmentType: null);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(
            Criteria(), FacetDimension.EmploymentType, ct);

        counts[et].ShouldBe(1);
        counts.Keys.ShouldNotContain(k => string.IsNullOrEmpty(k));
    }
}
