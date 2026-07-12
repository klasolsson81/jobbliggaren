using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-8b 8b.3 (CTO-bind Q2) — HTTP wiring for the template-catalog endpoint
// (GET /resumes/template-catalog). Static, non-PII reference data the mallbyggare's pickers consume.
// Proves auth-gating (401) and the 200→JSON shape (the four closed option groups, per-template
// AtsSafe, accent hex swatches). Not owner-scoped (no PII), so no cross-user test — every authed user
// gets the identical catalog.
[Collection("Api")]
public class ResumeTemplateCatalogEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"catalog-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task GET_template_catalog_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/resumes/template-catalog", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_template_catalog_returns_the_four_closed_option_groups()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/resumes/template-catalog", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Cacheable static reference data (CTO Q2 — changes only on deploy).
        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromHours(1));
        response.Headers.CacheControl.Private.ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Templates carry the Domain-sourced AtsSafe; MorkPanel is the one non-ATS-safe template.
        var templates = body.GetProperty("templates").EnumerateArray().ToList();
        templates.ShouldContain(t => t.GetProperty("name").GetString() == "Klar" && t.GetProperty("atsSafe").GetBoolean());
        templates.ShouldContain(t => t.GetProperty("name").GetString() == "MorkPanel" && !t.GetProperty("atsSafe").GetBoolean());

        // Accents carry a "#RRGGBB" swatch from the palette.
        var accents = body.GetProperty("accents").EnumerateArray().ToList();
        accents.Count.ShouldBeGreaterThanOrEqualTo(4);
        accents.ShouldAllBe(a => a.GetProperty("hex").GetString()!.StartsWith('#') && a.GetProperty("hex").GetString()!.Length == 7);

        // Font pairs are emitted though the FE defers the control (BE vocabulary SSOT).
        body.GetProperty("fontPairs").GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
        body.GetProperty("densities").GetArrayLength().ShouldBeGreaterThanOrEqualTo(3);
    }
}
