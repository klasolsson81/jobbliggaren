using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// C1 (ADR 0067 Platsbanken sök-paritet) — HTTP-vägen för de nya query-params
// ?occupationGroup= + ?municipality= (upprepad query-string → string[]).
// Verifierar endpoint-binding → ListJobAdsQuery → validator → ApplyCriteria mot
// riktig Testcontainers-Postgres. Speglar ListJobAdsFilterTests HTTP-mönster.
//
// RÖD tills JobAdsEndpoints binder occupationGroup/municipality + ApplyCriteria
// filtrerar på dem.
[Collection("Api")]
public class ListJobAdsOccupationGroupEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task SeedImportedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? municipalityConceptId,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";
        var addressJson = municipalityConceptId is null
            ? "null"
            : $"{{\"municipality_concept_id\":\"{municipalityConceptId}\"}}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"occupation_group\":{groupJson}," +
            $"\"workplace_address\":{addressJson}}}";

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

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
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
            "GruppMatch annons", groupMatch, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Ej match annons", groupOther, null, $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={groupMatch}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("GruppMatch annons");
    }

    [Fact]
    public async Task GET_job_ads_with_multi_occupationGroup_returns_union()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupA = $"grp{Guid.NewGuid():N}"[..16];
        var groupB = $"grp{Guid.NewGuid():N}"[..16];
        var groupOther = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("A", groupA, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("B", groupB, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("C", groupOther, null, $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={groupA}&occupationGroup={groupB}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, _) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GET_job_ads_with_municipality_filter_returns_only_matching_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        var knMatch = $"kn{Guid.NewGuid():N}"[..16];
        var knOther = $"kn{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "KommunMatch annons", null, knMatch, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "Ej match annons", null, knOther, $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?municipality={knMatch}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (totalCount, items) = await ReadPagedAsync(response, ct);
        totalCount.ShouldBe(1);
        items[0].GetProperty("title").GetString().ShouldBe("KommunMatch annons");
    }

    [Fact]
    public async Task GET_job_ads_with_invalid_occupationGroup_format_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Mellanslag bryter regex ^[A-Za-z0-9_-]{1,32}$.
        var response = await _client.GetAsync(
            "/api/v1/job-ads?occupationGroup=has%20space", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_with_invalid_municipality_format_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?municipality=åäö", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
