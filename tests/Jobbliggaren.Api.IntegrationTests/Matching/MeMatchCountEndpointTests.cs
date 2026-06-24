using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// ADR 0079 STEG 6 — the Översikt live-notis endpoint <c>GET /api/v1/me/match-count</c>,
/// end-to-end on the wired API. The WIRE scope: the endpoint is auth-gated
/// (<c>RequireAuthorization</c> + MeListReadPolicy), composes through the Mediator pipeline,
/// and returns a JSON <c>{ count: int }</c>. The COUNT logic (coherence with the list path,
/// the grade-SSOT cardinality, empty-grade semantics) is pinned exhaustively by
/// <see cref="MatchCountOracleTests"/>; this file proves the auth gate + the JSON contract.
/// <para>
/// Auth pattern mirrors <see cref="JobAds.ListJobAdsMatchGradeFilterEndpointTests"/>
/// (<see cref="AuthTestHelpers.RegisterAndGetSessionIdAsync"/> → Bearer session-id).
/// </para>
/// </summary>
[Collection("Api")]
public class MeMatchCountEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task GET_match_count_authed_returns_200_with_count_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/me/match-count", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The JSON contract: a single integer `count` (camelCase). A fresh user has no stated
        // occupation → the SSYK-gate in the handler returns honest 0 (never a mock number).
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("count", out var countProp).ShouldBeTrue(
            "Svaret ska bära ett `count`-fält (camelCase) — kontraktet FE-notisen läser.");
        countProp.ValueKind.ShouldBe(JsonValueKind.Number);
        countProp.GetInt32().ShouldBe(0,
            "En ny användare utan angivet yrke ska få honest 0 (SSYK-grinden i handlern) — " +
            "aldrig en fejkad mock-siffra.");
    }

    [Fact]
    public async Task GET_match_count_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header → RequireAuthorization rejects before the handler runs.
        var response = await _client.GetAsync("/api/v1/me/match-count", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
