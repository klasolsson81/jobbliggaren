using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Companies;

/// <summary>
/// #454 (ADR 0088) — POST /api/v1/companies/lookup end-to-end over the Fake provider (the
/// Development host registers <c>CompanyRegistry:Provider=Fake</c>) + the REAL Redis read-through
/// decorator (Testcontainers). Proves the wire contract: auth-gate, 200-with-status for
/// found/notFound (never-500 civic degradation), the 400 refuse for a personnummer-shaped org.nr
/// WITHOUT echoing the value (ADR 0088 D4 + security MUST), the D8(c) body-not-URL shape, the
/// enrichment fields, and the dedicated rate-limit policy attachment.
/// </summary>
[Collection("Api")]
public class CompanyLookupEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/companies/lookup";
    private const string FakeKnownOrgNr = "5560125790"; // FakeCompanyRegistry → "Volvo Aktiebolag"
    private const string FakeUnknownOrgNr = "5599999999"; // legal-entity-shaped, not in the fixture
    private const string PnrShapedOrgNr = "1901012384"; // third digit 0 → refused (D4)

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private Task<HttpResponseMessage> LookupAsync(string orgNr, CancellationToken ct) =>
        _client.PostAsJsonAsync(Endpoint, new { organizationNumber = orgNr }, ct);

    [Fact]
    public async Task POST_lookup_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await LookupAsync(FakeKnownOrgNr, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_lookup_known_company_returns_found_with_enrichment_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await LookupAsync(FakeKnownOrgNr, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Per-user enrichment must never land in a shared cache (security MUST).
        response.Headers.CacheControl!.ToString().ShouldContain("no-store");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("status").GetString().ShouldBe("found");
        json.GetProperty("organizationNumber").GetString().ShouldBe(FakeKnownOrgNr);
        json.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeFalse();
        json.GetProperty("companyName").GetString().ShouldBe("Volvo Aktiebolag");
        // No seeded ads for the fixture org.nr → the honest 0-ad story; fresh user has no stated
        // occupation → matching count is the not-assessed null; not following → null watch id.
        json.GetProperty("activeAdCount").GetInt32().ShouldBe(0);
        json.GetProperty("matchingAdCount").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("companyWatchId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_lookup_unknown_company_returns_200_notFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await LookupAsync(FakeUnknownOrgNr, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("status").GetString().ShouldBe("notFound");
        json.GetProperty("organizationNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("companyName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_lookup_malformed_org_number_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await LookupAsync("556012-5790", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_lookup_pnr_shaped_returns_400_without_echoing_the_value()
    {
        // ADR 0088 D4 (security-bound Posture A): refused BEFORE any registry/cache touch, and the
        // refusal payload never reflects the typed value back (a potential personnummer).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await LookupAsync(PnrShapedOrgNr, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldNotContain(PnrShapedOrgNr);
    }

    [Fact]
    public async Task POST_lookup_after_follow_surfaces_companyWatchId()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var follow = await _client.PostAsJsonAsync(
            "/api/v1/me/company-watches", new { organizationNumber = FakeKnownOrgNr }, ct);
        follow.StatusCode.ShouldBe(HttpStatusCode.Created);
        var followJson = await follow.Content.ReadFromJsonAsync<JsonElement>(ct);
        var watchId = followJson.GetProperty("id").GetString();

        var response = await LookupAsync(FakeKnownOrgNr, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("status").GetString().ShouldBe("found");
        json.GetProperty("companyWatchId").GetString().ShouldBe(watchId);
    }

    [Fact]
    public void Lookup_endpoint_has_the_dedicated_company_lookup_rate_limit_policy()
    {
        // ADR 0088 D7 — the endpoint must carry ITS OWN policy (least common mechanism/bulkhead:
        // a lookup is a potential upstream SCB call once the real adapter activates). Metadata-level
        // pin: a silently dropped .RequireRateLimiting would leave the endpoint on no/shared budget.
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var lookup = endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/v1/companies/lookup");

        var policy = lookup.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        policy.ShouldNotBeNull();
        policy.PolicyName.ShouldBe("company-lookup");
    }
}
