using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Epik #526 (ADR 0088) — the live search-preview endpoint <c>POST /api/v1/me/match-count-preview</c>,
/// end-to-end on the wired API. The WIRE scope: the endpoint is auth-gated
/// (<c>RequireAuthorization</c> + MatchCountPreviewPolicy), composes through the Mediator pipeline
/// (validator → handler), and returns a JSON <c>{ count: int }</c>. The count logic / coherence with
/// the /jobb search count lives in the Application/oracle tests; this file proves the auth gate, the
/// validator gate, and the JSON contract.
/// </summary>
[Collection("Api")]
public class MatchCountPreviewEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // Hoisted per CA1861 (no constant array args inline in the JSON bodies below).
    private static readonly string[] DevGroup = ["grp_dev"];
    private static readonly string[] Kommun0180 = ["kommun_0180"];
    private static readonly string[] FastEmployment = ["et_fast"];
    private static readonly string[] BadToken = ["bad id!"];

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task POST_preview_authed_empty_draft_returns_200_with_count_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Empty draft → the honest total (all active ads), never a not-assessed/null.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/match-count-preview", new { }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("count", out var countProp).ShouldBeTrue(
            "Svaret ska bära ett `count`-fält (camelCase) — kontraktet FE-räknaren läser.");
        countProp.ValueKind.ShouldBe(JsonValueKind.Number);
        countProp.GetInt32().ShouldBeGreaterThanOrEqualTo(0,
            "Sök-preview-counten är alltid ett konkret icke-negativt tal (aldrig null/tom body).");
    }

    [Fact]
    public async Task POST_preview_authed_valid_draft_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/match-count-preview",
            new
            {
                occupationGroups = DevGroup,
                municipalities = Kommun0180,
                employmentTypes = FastEmployment,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_preview_invalid_concept_id_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // A free-text / non-concept-id token is rejected by the validator (defense-in-depth;
        // a personnummer can never match the concept-id regex either).
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/match-count-preview",
            new { occupationGroups = BadToken },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_preview_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header → RequireAuthorization rejects before the handler runs.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/match-count-preview", new { }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
