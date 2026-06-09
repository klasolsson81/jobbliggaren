using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.RecentSearches;

// ADR 0060 — RecentJobSearches auto-capture + list/delete end-to-end mot
// Testcontainers Postgres. Auto-capture sker via RecentJobSearchCaptureBehavior
// när authenticated user kör GET /api/v1/job-ads med ICapturesRecentSearch-
// query-shape (q/occupationGroup/municipality/region/sortBy — C2-form).
//
// C2 (ADR 0067, CTO-dom (d) + architect F5/F6): yrkesgrupp-only- och
// kommun-only-sökningar capture:as nu (stänger C1:s LIVE-gap där guarden bara
// räknade Q/Ssyk/Region). DTO:n är additiv: deprecated ssykList/ssykLabels är
// ALLTID tomma; nya occupationGroupList/municipalityList + labels bär data.
[Collection("Api")]
public class RecentSearchesTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task GET_recent_searches_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/me/recent-searches", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Searching_jobs_captures_a_recent_search_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Trigga auto-capture genom att söka /api/v1/job-ads med kriterier.
        var searchResponse = await _client.GetAsync(
            "/api/v1/job-ads?q=backend&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        items.GetArrayLength().ShouldBe(1);

        var row = items[0];
        row.GetProperty("q").GetString().ShouldBe("backend");
        row.GetProperty("label").GetString().ShouldBe("backend");
    }

    [Fact]
    public async Task Searching_jobs_with_occupation_group_only_captures_a_recent_search_row()
    {
        // C1:s LIVE-gap: en ?occupationGroup=-sökning utan q capture:ades
        // aldrig (guarden räknade inte dimensionen). C2 stänger gapet.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var group = $"grp{Guid.NewGuid():N}"[..16];
        var searchResponse = await _client.GetAsync(
            $"/api/v1/job-ads?occupationGroup={group}&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);

        var row = items[0];
        row.GetProperty("q").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("occupationGroupList").EnumerateArray()
            .Select(e => e.GetString())
            .ShouldContain(group);
        // Deprecated fält består i wire-formen (FE-zod REQUIRED) men är ALLTID tomma.
        row.GetProperty("ssykList").GetArrayLength().ShouldBe(0);
        row.GetProperty("ssykLabels").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Searching_jobs_with_municipality_only_captures_a_recent_search_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var municipality = $"kn{Guid.NewGuid():N}"[..16];
        var searchResponse = await _client.GetAsync(
            $"/api/v1/job-ads?municipality={municipality}&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);

        var row = items[0];
        row.GetProperty("municipalityList").EnumerateArray()
            .Select(e => e.GetString())
            .ShouldContain(municipality);
        row.GetProperty("ssykList").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Re_searching_same_filter_bumps_existing_row_no_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await _client.GetAsync("/api/v1/job-ads?q=devops&page=1&pageSize=20", ct);
        await _client.GetAsync("/api/v1/job-ads?q=devops&page=1&pageSize=20", ct);
        await _client.GetAsync("/api/v1/job-ads?q=devops&page=1&pageSize=20", ct);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("q").GetString().ShouldBe("devops");
    }

    [Fact]
    public async Task DELETE_recent_search_removes_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await _client.GetAsync("/api/v1/job-ads?q=qa&page=1&pageSize=20", ct);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);
        var id = items[0].GetProperty("id").GetString()!;

        var deleteResponse = await _client.DeleteAsync($"/api/v1/me/recent-searches/{id}", ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var afterItems = await afterDelete.Content.ReadFromJsonAsync<JsonElement>(ct);
        afterItems.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task DELETE_other_users_recent_search_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        // User A skapar en RecentJobSearch
        await AuthenticateAsync(ct);
        await _client.GetAsync("/api/v1/job-ads?q=sales&page=1&pageSize=20", ct);
        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var aId = items[0].GetProperty("id").GetString()!;

        // User B autentiserar via fresh HttpClient + cookie-jar
        var clientB = factory.CreateClient();
        var bSessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(clientB, ct: ct);
        clientB.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bSessionId);

        var crossDelete = await clientB.DeleteAsync($"/api/v1/me/recent-searches/{aId}", ct);
        // ADR 0031 — exponera inte forbidden vs notfound i öppna svaret
        crossDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // User A:s rad är intakt
        var stillThere = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var stillItems = await stillThere.Content.ReadFromJsonAsync<JsonElement>(ct);
        stillItems.GetArrayLength().ShouldBe(1);
    }
}
