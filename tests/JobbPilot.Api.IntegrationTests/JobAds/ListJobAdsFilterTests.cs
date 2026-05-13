using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// F2-P9 (TD-70, CTO-rond 2026-05-13). Filter-suite för GET /api/v1/job-ads
// med ssyk / region / q. Mot Testcontainers Postgres (F2P9JobAdSearchColumns-
// migration aktiv) — verifierar att generated columns auto-populeras från
// raw_payload + att EF.Functions.Like-translation fungerar case-insensitivt.
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

    // Seed:ar JobAd via Import-factory med raw_payload som har ssyk + region
    // concept-ids på korrekt JSON-path. Generated columns (ssyk_concept_id,
    // region_concept_id) auto-populeras av Postgres vid INSERT — verifierar
    // hela kedjan från raw_payload till indexed column.
    private async Task SeedImportedJobAdAsync(
        string title,
        string description,
        string? ssykConceptId,
        string? regionConceptId,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(externalId, ssykConceptId, regionConceptId);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: description,
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    // Seed:ar manuell JobAd utan raw_payload — generated columns blir NULL,
    // får ingen entry i partial index, ska aldrig matchas av ssyk/region-filter.
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
        string externalId, string? ssykConceptId, string? regionConceptId)
    {
        // Bygger payload som matchar generated-column-paths i JobAdConfiguration:
        //   raw_payload->'occupation'->>'concept_id'              → ssyk_concept_id
        //   raw_payload->'workplace_address'->>'region_concept_id' → region_concept_id
        var ssykJson = ssykConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{ssykConceptId}\"}}";
        var regionJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";

        return $"{{\"id\":\"{externalId}\",\"occupation\":{ssykJson},\"workplace_address\":{regionJson}}}";
    }

    private static async Task<(int totalCount, JsonElement items)> ReadPagedAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (json.GetProperty("totalCount").GetInt32(), json.GetProperty("items"));
    }

    [Fact]
    public async Task GET_job_ads_with_ssyk_filter_returns_only_matching_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssykMatch = $"ssyk{Guid.NewGuid():N}"[..16];
        var ssykOther = $"ssyk{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "SsykMatch annons", "desc", ssykMatch, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Ej match annons", "desc", ssykOther, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/job-ads?ssyk={ssykMatch}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("SsykMatch annons");
    }

    [Fact]
    public async Task GET_job_ads_with_region_filter_returns_only_matching_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        var regionMatch = $"reg{Guid.NewGuid():N}"[..16];
        var regionOther = $"reg{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "RegionMatch annons", "desc", ssykConceptId: null, regionMatch,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Ej match annons", "desc", ssykConceptId: null, regionOther,
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
        var unique = Guid.NewGuid().ToString("N")[..12];
        await SeedImportedJobAdAsync(
            $"Backend {unique} developer", "ordinarie beskrivning",
            ssykConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Helt annan titel", "ordinarie beskrivning",
            ssykConceptId: null, regionConceptId: null,
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
        var unique = Guid.NewGuid().ToString("N")[..12];
        await SeedImportedJobAdAsync(
            "Vanlig titel A", $"Vi söker {unique} expert.",
            ssykConceptId: null, regionConceptId: null,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Vanlig titel B", "Ingen match här.",
            ssykConceptId: null, regionConceptId: null,
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
        var unique = Guid.NewGuid().ToString("N")[..8];
        await SeedImportedJobAdAsync(
            $"Senior {unique}Developer", "desc",
            ssykConceptId: null, regionConceptId: null,
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
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];
        var region = $"reg{Guid.NewGuid():N}"[..16];
        var qToken = Guid.NewGuid().ToString("N")[..10];

        // Match: ssyk + region + title innehåller qToken
        await SeedImportedJobAdAsync(
            $"Annons {qToken} target", "desc", ssyk, region,
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        // Endast ssyk-match (region annan, q saknas)
        await SeedImportedJobAdAsync(
            "Ssyk men ej region", "desc", ssyk, regionConceptId: $"reg{Guid.NewGuid():N}"[..16],
            externalId: $"ext-{Guid.NewGuid():N}", ct);
        // Ssyk + region match men q matchar inte
        await SeedImportedJobAdAsync(
            "Helt fel titel", "Helt fel beskrivning", ssyk, region,
            externalId: $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?ssyk={ssyk}&region={region}&q={qToken}", ct);

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
        var response = await _client.GetAsync($"/api/v1/job-ads?ssyk={nonExistent}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(0);
        items.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GET_job_ads_with_filter_total_count_reflects_filtered_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];

        // Seed:a 3 med matchande ssyk + 2 med annan ssyk
        for (var i = 0; i < 3; i++)
        {
            await SeedImportedJobAdAsync(
                $"Match {i}", "desc", ssyk, regionConceptId: null,
                externalId: $"ext-{Guid.NewGuid():N}", ct);
        }
        for (var i = 0; i < 2; i++)
        {
            await SeedImportedJobAdAsync(
                $"Annan {i}", "desc",
                ssykConceptId: $"ssyk{Guid.NewGuid():N}"[..16],
                regionConceptId: null,
                externalId: $"ext-{Guid.NewGuid():N}", ct);
        }

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/job-ads?ssyk={ssyk}&pageSize=2", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        // totalCount = filtrerad mängd (3), inte global (5+)
        json.GetProperty("totalCount").GetInt32().ShouldBe(3);
        // pageSize = 2 → 2 items på första sidan
        json.GetProperty("items").GetArrayLength().ShouldBe(2);
        json.GetProperty("totalPages").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GET_job_ads_with_invalid_ssyk_format_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Mellanslag bryter regex ^[A-Za-z0-9_-]{1,32}$
        var response = await _client.GetAsync("/api/v1/job-ads?ssyk=has%20space", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

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
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];

        // Manuell annons utan raw_payload → ssyk_concept_id NULL i DB
        await SeedManualJobAdAsync($"Manuell med {ssyk} i titel", "desc", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/job-ads?ssyk={ssyk}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, _) = await ReadPagedAsync(response, ct);
        // Filter på generated column = NULL → ingen match även om ssyk-strängen
        // råkar finnas i title.
        totalCount.ShouldBe(0);
    }
}
