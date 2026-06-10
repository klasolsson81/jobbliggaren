using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// ADR 0067 Beslut 4 (Platsbanken sök-paritet Fas D1) — per-option facet-counts.
// Verifierar IJobAdSearchQuery.FacetCountsAsync mot riktig Postgres
// (Testcontainers, ALDRIG EF-InMemory: GROUP BY mot STORED shadow-column +
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
[Collection("Api")]
public class JobAdFacetCountsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Seedar en importerad, AKTIV JobAd med valfria shadow-värden via raw_payload.
    // occupation_group är TOP-LEVEL; municipality + region bor BÅDA i
    // workplace_address (verifierat mot JobAdGeneratedColumnsTests + JobAdConfiguration).
    private async Task SeedAsync(
        string title,
        string? occupationGroup,
        string? municipality,
        string? region,
        CancellationToken ct,
        bool archived = false)
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

        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{occupationGroupJson}," +
            $"\"workplace_address\":{workplaceAddressJson}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
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
        string? q = null) =>
        new(
            OccupationGroup: occupationGroup ?? [],
            Municipality: municipality ?? [],
            Region: region ?? [],
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
}
