using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// F2-P9 (TD-70, CTO-rond 2026-05-13). Filter-suite för GET /api/v1/job-ads
// med occupationGroup / region / q. Mot Testcontainers Postgres
// (F2P9JobAdSearchColumns-migration aktiv) — verifierar att generated columns
// auto-populeras från raw_payload + att EF.Functions.Like-translation fungerar
// case-insensitivt.
//
// C1 (ADR 0067 Platsbanken sök-paritet) — Variant C nivåbyte: yrke-filtrets
// semantik flyttad från ssyk (occupation-name) till occupationGroup
// (ssyk-level-4, occupation_group_concept_id). De yrke-filtrerande testerna här
// targetar därför occupationGroup. Ssyk-no-op-regressionen ligger separat i
// ListJobAdsSsykNoOpTests.
[Collection("Api")]
public class ListJobAdsFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // Seed:ar JobAd via Import-factory med raw_payload som har occupation_group
    // + region concept-ids på korrekt JSON-path. Generated columns
    // (occupation_group_concept_id, region_concept_id) auto-populeras av Postgres
    // vid INSERT — verifierar hela kedjan från raw_payload till indexed column.
    private async Task SeedImportedJobAdAsync(
        string title,
        string description,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(externalId, occupationGroupConceptId, regionConceptId);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: description,
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    // Seed:ar manuell JobAd utan raw_payload — generated columns blir NULL,
    // får ingen entry i partial index, ska aldrig matchas av
    // occupationGroup/region-filter.
    private async Task SeedManualJobAdAsync(
        string title,
        string description,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var jobAd = JobAd.Create(
            title: title,
            company: Company.Create("Manual Company AB").Value,
            description: description,
            url: "https://example.com/manual",
            source: JobSource.Manual,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static string BuildRawPayload(
        string externalId, string? occupationGroupConceptId, string? regionConceptId)
    {
        // Bygger payload som matchar generated-column-paths i JobAdConfiguration:
        //   raw_payload->'occupation_group'->>'concept_id'         → occupation_group_concept_id (TOP-LEVEL)
        //   raw_payload->'workplace_address'->>'region_concept_id' → region_concept_id
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";
        var regionJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";

        return $"{{\"id\":\"{externalId}\",\"occupation_group\":{groupJson},\"workplace_address\":{regionJson}}}";
    }

    private static async Task<(int totalCount, JsonElement items)> ReadPagedAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (json.GetProperty("totalCount").GetInt32(), json.GetProperty("items"));
    }

    [Fact]
    public async Task GET_job_ads_with_occupationGroup_filter_returns_only_matching_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupMatch = $"grp{Guid.NewGuid():N}"[..16];
        var groupOther = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "GruppMatch annons", "desc", groupMatch, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Ej match annons", "desc", groupOther, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={groupMatch}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("GruppMatch annons");
    }

    [Fact]
    public async Task GET_job_ads_with_region_filter_returns_only_matching_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        var regionMatch = $"reg{Guid.NewGuid():N}"[..16];
        var regionOther = $"reg{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "RegionMatch annons", "desc", occupationGroupConceptId: null, regionMatch,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Ej match annons", "desc", occupationGroupConceptId: null, regionOther,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/job-ads?region={regionMatch}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("RegionMatch annons");
    }

    [Fact]
    public async Task GET_job_ads_with_q_filter_matches_title()
    {
        var ct = TestContext.Current.CancellationToken;
        var unique = TestIds.Token(12);
        await SeedImportedJobAdAsync(
            $"Backend {unique} developer", "ordinarie beskrivning",
            occupationGroupConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Helt annan titel", "ordinarie beskrivning",
            occupationGroupConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/job-ads?q={unique}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        var title = items[0].GetProperty("title").GetString();
        title.ShouldNotBeNull();
        title.ShouldContain(unique);
    }

    [Fact]
    public async Task GET_job_ads_with_q_filter_matches_description()
    {
        var ct = TestContext.Current.CancellationToken;
        var unique = TestIds.Token(12);
        await SeedImportedJobAdAsync(
            "Vanlig titel A", $"Vi söker {unique} expert.",
            occupationGroupConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Vanlig titel B", "Ingen match här.",
            occupationGroupConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/job-ads?q={unique}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("Vanlig titel A");
    }

    [Fact]
    public async Task GET_job_ads_with_q_filter_is_case_insensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var unique = TestIds.Token(8);
        await SeedImportedJobAdAsync(
            $"Senior {unique}Developer", "desc",
            occupationGroupConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        // söker i versaler — ska träffa "Developer" via LOWER()
        var upperQuery = $"{unique.ToUpperInvariant()}DEVELOPER";
        var response = await _client.GetAsync($"/api/v1/job-ads?q={upperQuery}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, _) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GET_job_ads_with_all_filters_combined_applies_AND_logic()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];
        var region = $"reg{Guid.NewGuid():N}"[..16];
        var qToken = TestIds.Token(10);

        // Match: occupationGroup + region + title innehåller qToken
        await SeedImportedJobAdAsync(
            $"Annons {qToken} target", "desc", group, region,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        // Endast grupp-match (region annan, q saknas)
        await SeedImportedJobAdAsync(
            "Grupp men ej region", "desc", group, regionConceptId: $"reg{Guid.NewGuid():N}"[..16],
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        // Grupp + region match men q matchar inte
        await SeedImportedJobAdAsync(
            "Helt fel titel", "Helt fel beskrivning", group, region,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={group}&region={region}&q={qToken}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        var combinedTitle = items[0].GetProperty("title").GetString();
        combinedTitle.ShouldNotBeNull();
        combinedTitle.ShouldContain(qToken);
    }

    [Fact]
    public async Task GET_job_ads_filter_matches_nothing_returns_empty_paged_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var nonExistent = $"none{Guid.NewGuid():N}"[..16];

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={nonExistent}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(0);
        items.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GET_job_ads_with_filter_total_count_reflects_filtered_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];

        // Seed:a 3 med matchande occupationGroup + 2 med annan grupp
        for (var i = 0; i < 3; i++)
        {
            await SeedImportedJobAdAsync(
                $"Match {i}", "desc", group, regionConceptId: null,
                externalId: $"ext-{Guid.NewGuid():N}", ct);
        }
        for (var i = 0; i < 2; i++)
        {
            await SeedImportedJobAdAsync(
                $"Annan {i}", "desc",
                occupationGroupConceptId: $"grp{Guid.NewGuid():N}"[..16],
                regionConceptId: null,
                externalId: $"ext-{Guid.NewGuid():N}", ct);
        }

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={group}&pageSize=2", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        // totalCount = filtrerad mängd (3), inte global (5+)
        json.GetProperty("totalCount").GetInt32().ShouldBe(3);
        // pageSize = 2 → 2 items på första sidan
        json.GetProperty("items").GetArrayLength().ShouldBe(2);
        json.GetProperty("totalPages").GetInt32().ShouldBe(2);
    }

    // C2 (CTO-dom (e)): GET_job_ads_with_invalid_ssyk_format_returns_400 är
    // borttagen — ?ssyk= binder inte längre (obunden param ignoreras → 200).
    // Se ListJobAdsSsykNoOpTests.GET_job_ads_with_previously_invalid_ssyk_format_returns_200
    // (motsvarande occupationGroup-grind finns i ListJobAdsOccupationGroupEndpointTests).

    [Fact]
    public async Task GET_job_ads_with_invalid_region_format_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?region=åäö", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_with_q_too_short_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?q=a", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_with_q_too_long_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var tooLong = new string('x', 101);
        var response = await _client.GetAsync($"/api/v1/job-ads?q={tooLong}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_ad_without_raw_payload_not_matched_by_filter()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];

        // Manuell annons utan raw_payload → occupation_group_concept_id NULL i DB
        await SeedManualJobAdAsync($"Manuell med {group} i titel", "desc", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={group}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, _) = await ReadPagedAsync(response, ct);
        // Filter på generated column = NULL → ingen match även om grupp-strängen
        // råkar finnas i title.
        totalCount.ShouldBe(0);
    }
}
