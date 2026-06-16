using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.SavedSearches;

// Fas 4 STEG B / B4 — the CV→SavedSearch confirm→create pipeline (ADR 0040 Beslut 4). The derive
// step (read-only taxonomy lookup) + the confirm step (creates a SavedSearch from the user's
// CONFIRMED ssyk-4 ids + a provenance event). This is B's POSITIVE acceptance test: confirmed
// user-chosen ids create exactly one SavedSearch carrying those ids — the structural/runtime
// "no auto-create from derivation" guards (DerivedSavedSearchInvariantTests +
// DeriverCorpusStressTests) stay green because the confirm handler takes plain ids, not the
// deriver result. sortBy is sent numerically (the API registers no JsonStringEnumConverter).
[Collection("Api")]
public class ConfirmDerivedSearchEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"confirm-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static object ConfirmBody(params string[] occupationGroup) =>
        new
        {
            name = "Systemutvecklare i Stockholm",
            occupationGroup = occupationGroup.Length == 0 ? Array.Empty<string>() : occupationGroup,
            sourceParsedResumeId = (Guid?)null,
            municipality = (string[]?)null,
            region = (string[]?)null,
            employmentType = (string[]?)null,
            worktimeExtent = (string[]?)null,
            q = (string?)null,
            sortBy = 0,
            notificationEnabled = true,
        };

    // ---- confirm-derived ----

    [Fact]
    public async Task POST_confirm_derived_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync(
            "/api/v1/saved-searches/confirm-derived", ConfirmBody("grp_aaa"), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_confirm_derived_with_empty_occupation_group_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.PostAsJsonAsync(
            "/api/v1/saved-searches/confirm-derived", ConfirmBody(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_confirm_derived_creates_one_saved_search_with_the_confirmed_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var confirm = await _client.PostAsJsonAsync(
            "/api/v1/saved-searches/confirm-derived", ConfirmBody("grp_aaa", "grp_bbb"), ct);

        confirm.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await confirm.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        // The created SavedSearch carries exactly the user's CONFIRMED occupation ids.
        var get = await _client.GetAsync($"/api/v1/saved-searches/{id}", ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("name").GetString().ShouldBe("Systemutvecklare i Stockholm");
        var groups = json.GetProperty("occupationGroup").EnumerateArray().Select(e => e.GetString()).ToList();
        groups.ShouldBe(["grp_aaa", "grp_bbb"]);
    }

    [Fact]
    public async Task POST_confirm_derived_with_malformed_concept_id_returns_400()
    {
        // The SearchCriteria per-id regex rejects this in the handler → the endpoint's non-NotFound
        // branch maps it to 400 over HTTP (not 500, not the EndsWith("NotFound") 404 branch).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.PostAsJsonAsync(
            "/api/v1/saved-searches/confirm-derived", ConfirmBody("not a valid id!"), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- derive ----

    [Fact]
    public async Task GET_derive_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/saved-searches/derive?title=Systemutvecklare", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_derive_empty_title_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync("/api/v1/saved-searches/derive", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_derive_valid_title_returns_200_with_candidates_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync("/api/v1/saved-searches/derive?title=Systemutvecklare", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Systemutvecklare");
        json.GetProperty("candidates").ValueKind.ShouldBe(JsonValueKind.Array);
    }
}
