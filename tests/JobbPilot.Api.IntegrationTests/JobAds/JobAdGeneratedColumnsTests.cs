using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// Fas B1 (Platsbanken sök-paritet, Klass 1). Verifierar de TVÅ nya STORED
// generated columns på job_ads mot riktig Postgres (Testcontainers):
//   occupation_group_concept_id ← raw_payload->'occupation_group'->>'concept_id'
//                                 (OBS top-level, EJ nested under occupation)
//   municipality_concept_id     ← raw_payload->'workplace_address'->>'municipality_concept_id'
//
// Detta är det KRITISKA testet (architect): EF-InMemory ignorerar
// HasComputedColumnSql(stored: true) → endast en relationell motor beräknar
// kolumnerna vid INSERT. InMemory skulle ge falska gröna (jfr
// feedback_ef_strongly_typed_vo_contains_translation). Speglar
// ListJobAdsFilterTests: [Collection("Api")], ApiFactory, JobAd.Import,
// AppDbContext-scope, raw_payload-bygge på exakt JSON-path.
[Collection("Api")]
public class JobAdGeneratedColumnsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Läs-DTO för SqlQueryRaw. EF härleder förväntade resultatkolumn-namn från
    // property-namnen via modellens snake_case-namnkonvention (MunicipalityConceptId
    // → municipality_concept_id) — därför SELECT:ar vi de råa snake_case-kolumnerna
    // UTAN alias (ett PascalCase-alias skulle göra att EF inte hittar kolumnen).
    private sealed record GeneratedColumnRow(
        string? SsykConceptId,
        string? RegionConceptId,
        string? OccupationGroupConceptId,
        string? MunicipalityConceptId);

    // Seedar en importerad JobAd och returnerar dess unika title (för readback).
    private async Task<string> SeedImportedAsync(
        string? ssyk,
        string? region,
        string? occupationGroup,
        string? municipality,
        CancellationToken ct)
    {
        var title = $"GenCol {Guid.NewGuid():N}";
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(externalId, ssyk, region, occupationGroup, municipality);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return title;
    }

    private async Task<GeneratedColumnRow> ReadGeneratedColumnsAsync(
        string title, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Postgres beräknar STORED-kolumnerna vid INSERT. Läs verbatim via raw
        // SQL (kringgår EF-materialisering helt) — alias matchar DTO-properties.
        var rows = await db.Database
            .SqlQueryRaw<GeneratedColumnRow>(
                """
                SELECT ssyk_concept_id,
                       region_concept_id,
                       occupation_group_concept_id,
                       municipality_concept_id
                FROM job_ads
                WHERE title = {0}
                """,
                title)
            .ToListAsync(ct);

        return rows.ShouldHaveSingleItem();
    }

    // Bygger raw_payload med korrekt JSON-form (speglar ListJobAdsFilterTests
    // .BuildRawPayload-stilen — enkel interpolation, inga raw string literals):
    //   occupation.concept_id                       → ssyk_concept_id
    //   workplace_address.region_concept_id         → region_concept_id
    //   occupation_group.concept_id  (TOP-LEVEL)    → occupation_group_concept_id
    //   workplace_address.municipality_concept_id   → municipality_concept_id
    // occupation_group är TOP-LEVEL, EJ nested under occupation (path-kontrakt).
    private static string BuildRawPayload(
        string externalId,
        string? ssyk,
        string? region,
        string? occupationGroup,
        string? municipality)
    {
        var occupationJson = ssyk is null
            ? "null"
            : $"{{\"concept_id\":\"{ssyk}\"}}";

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

        return $"{{\"id\":\"{externalId}\","
            + $"\"occupation\":{occupationJson},"
            + $"\"occupation_group\":{occupationGroupJson},"
            + $"\"workplace_address\":{workplaceAddressJson}}}";
    }

    [Fact]
    public async Task GeneratedColumns_ShouldPopulateAllFour_WhenPayloadHasAllConceptIds()
    {
        var ct = TestContext.Current.CancellationToken;
        const string ssyk = "Ssyk_uwa_111";
        const string region = "C aR_hRu_111";
        const string occupationGroup = "DJh5_yyF_hEM";
        const string municipality = "AvNB_uwa_6n6";

        var title = await SeedImportedAsync(ssyk, region, occupationGroup, municipality, ct);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        // Alla fyra STORED-kolumner populeras samtidigt från samma raw_payload.
        row.SsykConceptId.ShouldBe(ssyk);
        row.RegionConceptId.ShouldBe(region);
        row.OccupationGroupConceptId.ShouldBe(occupationGroup);
        row.MunicipalityConceptId.ShouldBe(municipality);
    }

    [Fact]
    public async Task GeneratedColumns_ShouldReadOccupationGroupFromTopLevelPath_WhenPresent()
    {
        var ct = TestContext.Current.CancellationToken;
        const string occupationGroup = "DJh5_yyF_hEM";

        var title = await SeedImportedAsync(
            ssyk: null, region: null, occupationGroup: occupationGroup, municipality: null, ct);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.OccupationGroupConceptId.ShouldBe(occupationGroup);
    }

    [Fact]
    public async Task GeneratedColumns_ShouldReadMunicipalityFromWorkplaceAddressPath_WhenPresent()
    {
        var ct = TestContext.Current.CancellationToken;
        const string municipality = "AvNB_uwa_6n6";

        var title = await SeedImportedAsync(
            ssyk: null, region: null, occupationGroup: null, municipality: municipality, ct);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.MunicipalityConceptId.ShouldBe(municipality);
    }

    [Fact]
    public async Task GeneratedColumns_ShouldBeNull_WhenPayloadHasNoOccupationGroupOrMunicipality()
    {
        var ct = TestContext.Current.CancellationToken;

        // Endast ssyk + region — varken occupation_group eller municipality.
        var title = await SeedImportedAsync(
            ssyk: "Ssyk_uwa_222", region: "CaRR_hRu_222",
            occupationGroup: null, municipality: null, ct);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.SsykConceptId.ShouldNotBeNull();
        row.RegionConceptId.ShouldNotBeNull();
        row.OccupationGroupConceptId.ShouldBeNull();
        row.MunicipalityConceptId.ShouldBeNull();
    }

    [Fact]
    public async Task OccupationGroupConceptId_ShouldBeNull_WhenOnlyNestedOccupationConceptIdPresent()
    {
        // Path-förväxlings-spärr: payloaden bär occupation.concept_id (ssyk) men
        // INGEN top-level occupation_group. occupation_group_concept_id MÅSTE bli
        // NULL — annars läser computed-sql fel JSON-path (nested under occupation
        // istället för top-level). Detta är kärnan i Fas B1-path-kontraktet.
        var ct = TestContext.Current.CancellationToken;

        var title = await SeedImportedAsync(
            ssyk: "Ssyk_uwa_333", region: null,
            occupationGroup: null, municipality: null, ct);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.SsykConceptId.ShouldBe("Ssyk_uwa_333");
        row.OccupationGroupConceptId.ShouldBeNull();
    }
}
