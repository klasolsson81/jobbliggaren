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

// Fas E2c (ADR 0067 Beslut 4) — HTTP-vägen för GET /api/v1/job-ads/facet-counts:
// endpoint-binding (FacetDimension per namn, repeterade filter-listor) →
// GetFacetCountsQuery → validator → FacetCountsAsync mot riktig Testcontainers-
// Postgres. Speglar ListJobAdsOccupationGroupEndpointTests HTTP-mönster.
[Collection("Api")]
public class FacetCountsEndpointTests(ApiFactory factory)
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
        string title, string occupationGroupConceptId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{{\"concept_id\":\"{occupationGroupConceptId}\"}}}}";

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

    [Fact]
    public async Task GET_facet_counts_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync(
            "/api/v1/job-ads/facet-counts?dimension=OccupationGroup", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_facet_counts_with_unknown_dimension_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            "/api/v1/job-ads/facet-counts?dimension=NotADimension", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_facet_counts_with_numeric_out_of_range_dimension_returns_400_not_500()
    {
        // Enum-bindningen accepterar numeriska strängar utanför definierad
        // mängd (?dimension=7 binder) — validatorns IsInEnum() ska ge rent 400
        // i stället för Infrastructure-switchens throw → 500 (E2c-architect §1).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            "/api/v1/job-ads/facet-counts?dimension=7", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_facet_counts_without_dimension_returns_400()
    {
        // Minimal-API:s binding-fel för utelämnad non-nullable värdetyp-param
        // → 400 (code-reviewer Minor 2 — låser bindnings-beteendet).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync("/api/v1/job-ads/facet-counts", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_facet_counts_with_cap_exceeding_list_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // 401 st > MaxConceptIds (400) → validator-cap.
        var qs = string.Join("&", Enumerable.Range(0, 401).Select(i => $"region=r{i}"));
        var response = await _client.GetAsync(
            $"/api/v1/job-ads/facet-counts?dimension=Municipality&{qs}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_facet_counts_happy_path_returns_dict_shape_with_seeded_group()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        await SeedImportedJobAdAsync("FacetEndpointAd", grp, ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads/facet-counts?dimension=OccupationGroup", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldContain("private");
        response.Headers.CacheControl.ToString().ShouldContain("no-store");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        json.GetProperty(grp).GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }
}
