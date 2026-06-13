using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.SavedSearches;

// F2 Saved Searches (ADR 0039). End-to-end mot Testcontainers Postgres:
// JobSeeker-scoping, skapande, lista (egna), hämta, run. Run-semantik är
// query utan skriv-sidoeffekt (ADR 0039 Beslut 2).
[Collection("Api")]
public class SavedSearchesTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // sortBy skickas numeriskt: API:t registrerar ingen JsonStringEnumConverter
    // för request-body-bindning, så enum-fält i POST/PATCH-body måste vara
    // numeriska (0 = PublishedAtDesc). Se OBSERVATION i test-rapport.
    // C2 (CTO-dom (e) + architect F6): body-formen byter ssyk →
    // occupationGroup + municipality.
    private static object CreateBody(string name) => new
    {
        name,
        occupationGroup = new[] { "grp_12345" },
        municipality = (string[]?)null,
        region = (string[]?)null,
        q = "backend",
        sortBy = 0,
        notificationEnabled = false,
    };

    private async Task<string> CreateSavedSearchAsync(string name, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/saved-searches", CreateBody(name), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task GET_saved_searches_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/saved-searches", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_saved_search_returns_201_with_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/saved-searches", CreateBody("Mitt sök"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("id").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_saved_search_with_no_criteria_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var body = new
        {
            name = "Tomt",
            occupationGroup = (string[]?)null,
            municipality = (string[]?)null,
            region = (string[]?)null,
            q = (string?)null,
            sortBy = 0,
            notificationEnabled = false,
        };
        var response = await _client.PostAsJsonAsync("/api/v1/saved-searches", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_saved_search_with_legacy_ssyk_only_body_returns_400()
    {
        // C2 (architect F5.5): en hypotetisk gammal klient som POST:ar "ssyk"
        // får fältet tyst ignorerat (System.Text.Json default) → utan annat
        // kriterium blir det SearchCriteria.Empty-400 — korrekt fail-säkert,
        // ingen tyst halvspara.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var body = new
        {
            name = "Legacy-klient",
            ssyk = new[] { "12345" },   // okänd JSON-prop efter C2 — ignoreras
            sortBy = 0,
            notificationEnabled = false,
        };
        var response = await _client.PostAsJsonAsync("/api/v1/saved-searches", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_saved_searches_lists_only_own()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateSavedSearchAsync("Lista mig", ct);

        var response = await _client.GetAsync("/api/v1/saved-searches", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        json.EnumerateArray()
            .Any(s => s.GetProperty("id").GetString() == id)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task GET_saved_search_by_id_returns_dto()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateSavedSearchAsync("Hämta mig", ct);

        var response = await _client.GetAsync($"/api/v1/saved-searches/{id}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("id").GetString().ShouldBe(id);
        json.GetProperty("name").GetString().ShouldBe("Hämta mig");
        // ADR 0039 Beslut 2 — run skriver inte lastRunAt; vid skapande är den null.
        json.GetProperty("lastRunAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_run_saved_search_returns_paged_result()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateSavedSearchAsync("Kör mig", ct);

        var response = await _client.PostAsync($"/api/v1/saved-searches/{id}/run", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetProperty("totalCount").ValueKind.ShouldBe(JsonValueKind.Number);
        json.GetProperty("page").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task POST_run_does_not_write_last_run_at()
    {
        // ADR 0039 Beslut 2: run är query — ingen skriv-sidoeffekt.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateSavedSearchAsync("Run-no-write", ct);

        var runResponse = await _client.PostAsync($"/api/v1/saved-searches/{id}/run", null, ct);
        runResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/v1/saved-searches/{id}", ct);
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("lastRunAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task PATCH_then_GET_reflects_partial_update()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateSavedSearchAsync("Före byte", ct);

        var patch = await _client.PatchAsJsonAsync(
            $"/api/v1/saved-searches/{id}",
            new { name = "Efter byte", notificationEnabled = (bool?)null, criteria = (object?)null },
            ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/v1/saved-searches/{id}", ct);
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("name").GetString().ShouldBe("Efter byte");
    }

    [Fact]
    public async Task DELETE_then_GET_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateSavedSearchAsync("Radera mig", ct);

        var delete = await _client.DeleteAsync($"/api/v1/saved-searches/{id}", ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/v1/saved-searches/{id}", ct);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
