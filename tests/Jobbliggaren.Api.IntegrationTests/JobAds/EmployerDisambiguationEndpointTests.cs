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

// ADR 0087 D6/D8(c) (#311 PR-2b C2) — the employer-disambiguation endpoint
// GET /api/v1/job-ads/employers?q=<name>, end-to-end on the wired API. Proves the WIRE contract:
// the auth gate, the validator 400, and — the load-bearing assertion — the sole-prop personnummer
// guard surfacing correctly THROUGH the full pipeline (a personnummer-shaped org.nr comes back as
// null + IsProtectedIdentity=true; a legal-entity org.nr comes back verbatim). The projection
// mechanics (ILIKE/GROUP BY/count/cap/order) are pinned at EmployerDisambiguationQueryTests; this
// file pins that the guard is not bypassed on the real request path.
[Collection("Api")]
public class EmployerDisambiguationEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task SeedAdAsync(
        string organizationNumber, string companyName, string externalId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"employer\":" +
            $"{{\"name\":\"{companyName}\",\"organization_number\":\"{organizationNumber}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Utvecklare",
            company: Company.Create(companyName).Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static string NewBrand() => "Disam" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task GET_employers_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/job-ads/employers?q=Volvo", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_employers_too_short_q_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads/employers?q=a", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_employers_valid_q_no_matches_returns_200_empty_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // A brand token that matches nothing → a valid empty result, not a 404 / null-body
        // (guards the minimal-API Results.Ok(null)-empty-body trap: the array must serialize as []).
        var response = await _client.GetAsync($"/api/v1/job-ads/employers?q={NewBrand()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        rows.ValueKind.ShouldBe(JsonValueKind.Array);
        rows.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GET_employers_masks_sole_prop_org_nr_but_surfaces_legal_entity_on_the_wire()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var brand = NewBrand();
        const string legalOrgNr = "5566070707";     // 3rd digit '6' → legal entity
        const string solePropOrgNr = "8501010101";  // 3rd digit '0' → personnummer-shaped

        await SeedAdAsync(legalOrgNr, $"{brand} Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedAdAsync(solePropOrgNr, $"{brand} Enskild firma", $"ext-{Guid.NewGuid():N}", ct);

        var response = await _client.GetAsync($"/api/v1/job-ads/employers?q={brand}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // org.nr responses must never be shared-cached (private + no-store — vary per term/corpus/auth).
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var byName = rows.EnumerateArray()
            .ToDictionary(e => e.GetProperty("companyName").GetString()!, e => e);

        byName.Count.ShouldBe(2);

        var legal = byName[$"{brand} Cars AB"];
        legal.GetProperty("organizationNumber").GetString().ShouldBe(legalOrgNr);
        legal.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeFalse();

        var soleProp = byName[$"{brand} Enskild firma"];
        // The raw personnummer-shaped org.nr NEVER reaches the wire.
        soleProp.GetProperty("organizationNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        soleProp.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeTrue();
    }
}
