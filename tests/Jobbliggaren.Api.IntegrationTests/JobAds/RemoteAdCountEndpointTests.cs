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

// #551 PR-B D7 — the HTTP path for GET /api/v1/job-ads/remote-count: endpoint binding
// (non-location filter lists) → GetRemoteAdCountQuery → validator → CountAsync (remote=true,
// location excluded) against real Testcontainers-Postgres. Mirrors FacetCountsEndpointTests.
[Collection("Api")]
public class RemoteAdCountEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task SeedAsync(
        string occupationGroup, string? municipality, bool remote, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";
        var addressJson = municipality is null
            ? "null"
            : $"{{\"municipality_concept_id\":\"{municipality}\"}}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}}," +
            $"\"workplace_address\":{addressJson}}}";

        var jobAd = JobAd.Import(
            title: "RemoteCountAd",
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.From(
                occupationGroup: occupationGroup, municipality: municipality, remote: remote),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task GET_remote_count_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/job-ads/remote-count", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_remote_count_with_cap_exceeding_list_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var qs = string.Join("&", Enumerable.Range(0, 401).Select(i => $"occupationGroup=g{i}"));
        var response = await _client.GetAsync($"/api/v1/job-ads/remote-count?{qs}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_remote_count_counts_only_remote_ads_and_ignores_location()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // A run-unique occupation group isolates this run's ads over the shared Api DB.
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        await SeedAsync(grp, municipality: "GbgKommun", remote: false, ct); // located, on-site
        await SeedAsync(grp, municipality: null, remote: true, ct);          // remote, location-less

        var response = await _client.GetAsync(
            $"/api/v1/job-ads/remote-count?occupationGroup={grp}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldContain("private");
        response.Headers.CacheControl.ToString().ShouldContain("no-store");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        // Only the remote ad counts; the on-site GBG ad in the same group does not.
        json.GetProperty("count").GetInt32().ShouldBe(1);
    }
}
