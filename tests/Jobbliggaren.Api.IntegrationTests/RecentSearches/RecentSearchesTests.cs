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

        // #1430 — labeln är struktur, och dess enums når wire:n som NAMN, inte ordinaler.
        // Det är hela vägen ut, och det enda stället serialiseringsformen mäts: utan
        // JsonStringEnumConverter skickar System.Text.Json heltal, och då hade en omordning
        // inuti en enum tyst bytt betydelse på ett kontrakt zod-spegeln läser vid namn.
        var label = row.GetProperty("label");
        label.GetProperty("kind").GetString().ShouldBe("Query");
        label.GetProperty("join").GetString().ShouldBe("None");

        var parts = label.GetProperty("parts");
        parts.GetArrayLength().ShouldBe(1);
        parts[0].GetProperty("kind").GetString().ShouldBe("Named");
        parts[0].GetProperty("text").GetString().ShouldBe("backend");
        parts[0].GetProperty("moreCount").GetInt32().ShouldBe(0);
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
        // #1407: the false polarity of the distans axis must SERIALISE, not vanish.
        // Searching_jobs_with_remote_only_persists_the_remote_flag_and_surfaces_it pins
        // true; a field emitted only when true would pass that one while leaving the FE
        // schema without a value to read.
        row.GetProperty("remote").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Searching_jobs_with_employer_only_persists_org_nr_to_column_and_surfaces_it_for_replay()
    {
        // #311 PR-2b C1 (ADR 0087 D6): a committed ?employer= search captures a RecentJobSearch AND
        // persists the org.nr into the employer_list text[] column — the ONLY DB-level proof of the
        // shadow-backing-field + migration round-trip (the ListRecentSearches unit tests use EF
        // In-Memory, which never exercises text[]). #1471: the axis ALSO reaches the wire, as
        // `employerList`, so the replay href can carry it — the same three legs the remote sibling
        // below asserts. What may reach it is a legal-entity org.nr only (ADR 0087 D8(c), masked
        // arm — EmployerAxisGate); the label never carries the value under any form.
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
        // ...and the org.nr reaches the wire under exactly one name, the replay's.
        items[0].GetProperty("employerList").EnumerateArray()
            .Select(e => e.GetString())
            .ShouldBe([orgNr]);
        // The LABEL never carries it. Asserted on the VALUE, in the one subtree that is rendered
        // as text on three surfaces: the unit-level pin (Handle_NeverPutsTheEmployerOrgNumberInTheLabel)
        // reasons about the label's shape, this reads what System.Text.Json actually emitted.
        items[0].GetProperty("label").GetRawText().Contains(orgNr, StringComparison.Ordinal).ShouldBeFalse(
            "the recent-search label may name the employer axis but never its value — for an "
            + "enskild firma the value is the holder's personnummer (#841).");

        // The employer_list text[] column round-trips through real Postgres: read this user's row.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
        var recent = await db.RecentJobSearches.AsNoTracking()
            .Where(r => r.JobSeekerId == seeker.Id).ToListAsync(ct);
        recent.ShouldHaveSingleItem().Employer.ShouldBe([orgNr]);
    }

    [Fact]
    public async Task Searching_jobs_with_remote_only_persists_the_remote_flag_and_surfaces_it()
    {
        // #551 PR-D (ADR 0087 D6-paritet): a committed ?remote=true search captures a RecentJobSearch
        // AND persists the distans-axis into the remote bool column — the DB-level proof of the scalar-
        // column round-trip end-to-end (the ListRecentSearches unit tests use EF In-Memory).
        // #1407: the axis now ALSO reaches the wire, so the replay href can carry it. That is the whole
        // round-trip a user sees — column write, projection, wire — and it is asserted on both legs.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", ct);
        var userId = Guid.Parse(me.GetProperty("userId").GetString()!);

        // ASP.NET bool-binding kräver ?remote=true (INTE "on"; FE mappar rutt-flaggan ?distans=on hit).
        var searchResponse = await _client.GetAsync(
            "/api/v1/job-ads?remote=true&commit=true&page=1&pageSize=20", ct);
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The recent-search row is captured (the default-browse guard now counts remote)...
        var listResponse = await _client.GetAsync("/api/v1/me/recent-searches", ct);
        var items = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBe(1);
        // ...and remote IS on the wire, true (#1407 — the replay reproduces what the count counted).
        items[0].GetProperty("remote").GetBoolean().ShouldBeTrue();

        // The remote bool column round-trips through real Postgres: read this user's row.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
        var recent = await db.RecentJobSearches.AsNoTracking()
            .Where(r => r.JobSeekerId == seeker.Id).ToListAsync(ct);
        recent.ShouldHaveSingleItem().Remote.ShouldBeTrue();
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
