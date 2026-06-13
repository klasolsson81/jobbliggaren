using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// Fas B1+B2 (Platsbanken sök-paritet, Klass 1 + Klass 2). Verifierar STORED
// generated columns på job_ads mot riktig Postgres (Testcontainers):
//   B1: occupation_group_concept_id ← raw_payload->'occupation_group'->>'concept_id'
//                                     (OBS top-level, EJ nested under occupation)
//   B1: municipality_concept_id     ← raw_payload->'workplace_address'->>'municipality_concept_id'
//   B2: employment_type_concept_id  ← raw_payload->'employment_type'->>'concept_id'
//   B2: worktime_extent_concept_id  ← raw_payload->'working_hours_type'->>'concept_id'
//                                     (NAMNGLAPP: kolumn worktime_extent ↔ payload working_hours_type)
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
    // Fas B2 utökar DTO:n med två nya STORED-kolumner (Klass 2):
    //   employment_type_concept_id ← raw_payload->'employment_type'->>'concept_id'
    //   worktime_extent_concept_id ← raw_payload->'working_hours_type'->>'concept_id'
    // NAMNGLAPP: kolumnen heter worktime_extent_concept_id men läser payload-path
    // 'working_hours_type' (taxonomi-namn ≠ wire-key). Se nya [Fact]-metoder nedan.
    private sealed record GeneratedColumnRow(
        string? SsykConceptId,
        string? RegionConceptId,
        string? OccupationGroupConceptId,
        string? MunicipalityConceptId,
        string? EmploymentTypeConceptId,
        string? WorktimeExtentConceptId);

    // Seedar en importerad JobAd och returnerar dess unika title (för readback).
    private async Task<string> SeedImportedAsync(
        string? ssyk,
        string? region,
        string? occupationGroup,
        string? municipality,
        CancellationToken ct,
        string? employmentType = null,
        string? workingHoursType = null)
    {
        var title = $"GenCol {Guid.NewGuid():N}";
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, ssyk, region, occupationGroup, municipality,
            employmentType, workingHoursType);

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
                       municipality_concept_id,
                       employment_type_concept_id,
                       worktime_extent_concept_id
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
    //   employment_type.concept_id  (TOP-LEVEL)    → employment_type_concept_id   (B2)
    //   working_hours_type.concept_id (TOP-LEVEL)   → worktime_extent_concept_id   (B2)
    // employment_type + working_hours_type är TOP-LEVEL (speglar occupation_group),
    // EJ nested under conditions. NAMNGLAPP: payload-key working_hours_type matchar
    // kolumn worktime_extent_concept_id.
    private static string BuildRawPayload(
        string externalId,
        string? ssyk,
        string? region,
        string? occupationGroup,
        string? municipality,
        string? employmentType = null,
        string? workingHoursType = null)
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

        var employmentTypeJson = employmentType is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentType}\"}}";

        // OBS: wire-keyn är working_hours_type (ej worktime_extent) — kolumnen
        // worktime_extent_concept_id läser DENNA path.
        var workingHoursTypeJson = workingHoursType is null
            ? "null"
            : $"{{\"concept_id\":\"{workingHoursType}\"}}";

        return $"{{\"id\":\"{externalId}\","
            + $"\"occupation\":{occupationJson},"
            + $"\"occupation_group\":{occupationGroupJson},"
            + $"\"workplace_address\":{workplaceAddressJson},"
            + $"\"employment_type\":{employmentTypeJson},"
            + $"\"working_hours_type\":{workingHoursTypeJson}}}";
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

    // ── Fas B2 (Platsbanken sök-paritet, Klass 2) ─────────────────────────────
    // Två nya STORED generated columns mot riktig Postgres (Testcontainers):
    //   employment_type_concept_id ← raw_payload->'employment_type'->>'concept_id'
    //   worktime_extent_concept_id ← raw_payload->'working_hours_type'->>'concept_id'
    // KÄRNAN: kolumn worktime_extent_concept_id läser payload-path
    // 'working_hours_type' (namnglapp). Om HasComputedColumnSql copy-paste-glider
    // till '->'worktime_extent'->>...' blir kolumnen ALLTID NULL — dessa tester
    // fångar det. EF-InMemory ignorerar STORED → endast Postgres beräknar
    // (samma falsk-grön-risk som B1).

    [Fact]
    public async Task GeneratedColumns_ShouldPopulateEmploymentAndWorktime_WhenPayloadHasBothConceptIds()
    {
        // Populering (ny B2-payload-form): employment_type + working_hours_type
        // top-level → båda nya kolumner populeras. Detta är test 1 — fångar
        // namnglapp-glidningen direkt (worktime_extent_concept_id läser
        // working_hours_type-path).
        var ct = TestContext.Current.CancellationToken;
        const string employmentType = "PFZr_Syz_cUq"; // Tillsvidareanställning
        const string workingHoursType = "6YE1_gAC_R2G"; // Heltid

        var title = await SeedImportedAsync(
            ssyk: null, region: null, occupationGroup: null, municipality: null, ct,
            employmentType: employmentType, workingHoursType: workingHoursType);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.EmploymentTypeConceptId.ShouldBe(employmentType);
        // worktime_extent_concept_id MÅSTE bära working_hours_type-värdet.
        row.WorktimeExtentConceptId.ShouldBe(workingHoursType);
    }

    [Fact]
    public async Task WorktimeExtentConceptId_ShouldReadFromWorkingHoursTypePath_WhenPresent()
    {
        // NAMNGLAPP-spärr (isolerat): bevisar att worktime_extent_concept_id
        // läser raw_payload->'working_hours_type', INTE ->'worktime_extent'.
        // Endast working_hours_type seedas (employment_type null) → kolumnen
        // måste ändå populeras. Om SQL pekar på 'worktime_extent'-path (som
        // inte finns i payloaden) blir kolumnen NULL → testet rött.
        var ct = TestContext.Current.CancellationToken;
        const string workingHoursType = "6YE1_gAC_R2G";

        var title = await SeedImportedAsync(
            ssyk: null, region: null, occupationGroup: null, municipality: null, ct,
            employmentType: null, workingHoursType: workingHoursType);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.WorktimeExtentConceptId.ShouldBe(workingHoursType);
        row.EmploymentTypeConceptId.ShouldBeNull();
    }

    [Fact]
    public async Task EmploymentTypeConceptId_ShouldReadFromEmploymentTypePath_WhenPresent()
    {
        // Isolerat: employment_type_concept_id läser raw_payload->'employment_type'.
        var ct = TestContext.Current.CancellationToken;
        const string employmentType = "PFZr_Syz_cUq";

        var title = await SeedImportedAsync(
            ssyk: null, region: null, occupationGroup: null, municipality: null, ct,
            employmentType: employmentType, workingHoursType: null);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        row.EmploymentTypeConceptId.ShouldBe(employmentType);
        row.WorktimeExtentConceptId.ShouldBeNull();
    }

    [Fact]
    public async Task EmploymentAndWorktimeColumns_ShouldBeNull_WhenPayloadIsB1EraForm()
    {
        // NULL-spärr (gammal B1-eran payload-form utan employment_type/
        // working_hours_type-keys): båda nya kolumner == NULL. Bevisar grace-
        // degradering + att partial-index/filter exkluderar dessa rader (samma
        // 0-rad-backfill som migration F6P7 förlitar sig på). Seedar B1-fält
        // (occupation_group + municipality) för att visa att de OPÅVERKADE
        // populeras medan de nya förblir NULL.
        var ct = TestContext.Current.CancellationToken;

        var title = await SeedImportedAsync(
            ssyk: "Ssyk_uwa_444", region: null,
            occupationGroup: "DJh5_yyF_hEM", municipality: "AvNB_uwa_6n6", ct,
            employmentType: null, workingHoursType: null);

        var row = await ReadGeneratedColumnsAsync(title, ct);

        // B1-kolumner populeras oförändrat.
        row.OccupationGroupConceptId.ShouldBe("DJh5_yyF_hEM");
        row.MunicipalityConceptId.ShouldBe("AvNB_uwa_6n6");
        // B2-kolumner förblir NULL (gammal payload saknar keys).
        row.EmploymentTypeConceptId.ShouldBeNull();
        row.WorktimeExtentConceptId.ShouldBeNull();
    }
}
