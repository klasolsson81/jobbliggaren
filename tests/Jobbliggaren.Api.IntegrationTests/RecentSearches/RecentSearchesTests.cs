using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

// ADR 0060 — RecentJobSearches auto-capture + list/delete end-to-end mot
// Testcontainers Postgres. Auto-capture sker via RecentJobSearchCaptureBehavior
// när authenticated user kör GET /api/v1/job-ads med ICapturesRecentSearch-
// query-shape (q/occupationGroup/municipality/region/sortBy — C2-form).
//
// C2 (ADR 0067, CTO-dom (d) + architect F5/F6): yrkesgrupp-only- och
// kommun-only-sökningar capture:as nu (stänger C1:s LIVE-gap där guarden bara
// räknade Q/Ssyk/Region). E2b: C2-shimmet (ssykList/ssykLabels) är borttaget
// ur wire-formen — frånvaron vakthund-asserteras nedan (TryGetProperty).
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
            "/api/v1/job-ads?q=backend&commit=true&page=1&pageSize=20", ct);
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
    public async Task Live_search_without_commit_flag_does_not_capture()
    {
        // E2j (ADR 0060 amend 2026-06-12): live-`router.replace` per ord
        // (commit utelämnad) får ALDRIG capturera — det var E2i:s defekt
        // (cap=20 fylldes av mellanstegsspam, data-minimerings-regression).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var searchResponse = await _client.GetAsync(
            "/api/v1/job-ads?q=systemutvecklare&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        items.GetArrayLength().ShouldBe(0);
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
            $"/api/v1/job-ads?occupationGroup={group}&commit=true&page=1&pageSize=20", ct);
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
        // E2b: C2-shimmet (ssykList/ssykLabels) borttaget ur wire-formen —
        // fälten får INTE återuppstå (FE-zod frikopplad sedan E2a).
        row.TryGetProperty("ssykList", out _).ShouldBeFalse();
        row.TryGetProperty("ssykLabels", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Searching_jobs_with_municipality_only_captures_a_recent_search_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var municipality = $"kn{Guid.NewGuid():N}"[..16];
        var searchResponse = await _client.GetAsync(
            $"/api/v1/job-ads?municipality={municipality}&commit=true&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);

        var row = items[0];
        row.GetProperty("municipalityList").EnumerateArray()
            .Select(e => e.GetString())
            .ShouldContain(municipality);
        row.TryGetProperty("ssykList", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Searching_jobs_with_employer_only_persists_org_nr_to_column_without_surfacing_it()
    {
        // #311 PR-2b C1 (ADR 0087 D6): a committed ?employer= search captures a RecentJobSearch AND
        // persists the org.nr into the employer_list text[] column — the ONLY DB-level proof of the
        // shadow-backing-field + migration round-trip (the ListRecentSearches unit tests use EF
        // In-Memory, which never exercises text[]). The org.nr is deliberately NOT surfaced on the
        // wire (RecentJobSearchDto has no employer field — ADR 0087 D8(c): a user-owned org.nr is
        // never displayed un-flagged), so the round-trip is verified by reading the column directly.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", ct);
        var userId = Guid.Parse(me.GetProperty("userId").GetString()!);
        const string orgNr = "5566010101";

        var searchResponse = await _client.GetAsync(
            $"/api/v1/job-ads?employer={orgNr}&commit=true&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The recent-search row is captured (the default-browse guard now counts employer)...
        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);
        // ...but the org.nr is NOT on the wire (no employer/employerList field — D8(c) surfacing guard).
        items[0].TryGetProperty("employer", out _).ShouldBeFalse();
        items[0].TryGetProperty("employerList", out _).ShouldBeFalse();

        // The employer_list text[] column round-trips through real Postgres: read this user's row.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
        var recent = await db.RecentJobSearches.AsNoTracking()
            .Where(r => r.JobSeekerId == seeker.Id).ToListAsync(ct);
        recent.ShouldHaveSingleItem().Employer.ShouldBe([orgNr]);
    }

    [Fact]
    public async Task Re_searching_same_filter_bumps_existing_row_no_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await _client.GetAsync("/api/v1/job-ads?q=devops&commit=true&page=1&pageSize=20", ct);
        await _client.GetAsync("/api/v1/job-ads?q=devops&commit=true&page=1&pageSize=20", ct);
        await _client.GetAsync("/api/v1/job-ads?q=devops&commit=true&page=1&pageSize=20", ct);

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

        await _client.GetAsync("/api/v1/job-ads?q=qa&commit=true&page=1&pageSize=20", ct);

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
        await _client.GetAsync("/api/v1/job-ads?q=sales&commit=true&page=1&pageSize=20", ct);
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
