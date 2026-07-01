using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — end-to-end against Testcontainers Postgres: follow/unfollow/list an
/// employer by org.nr. Proves the things InMemory cannot — the value-converted
/// <c>OrganizationNumber</c> equality translates to SQL (the idempotent + resurrect paths depend on
/// it), the active-partial UNIQUE holds, soft-delete + resurrect keep one row, and the personnummer
/// guard (FORK C1 / D8(c)) masks a sole-prop org.nr in the surfaced list.
/// </summary>
[Collection("Api")]
public class CompanyWatchesTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";
    private const string LegalOrgNr = "5592804784";       // third digit 9 → legal entity
    private const string SoleProprietorOrgNr = "9001011234"; // YYMMDD 900101 → personnummer-shaped

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task<string> FollowAsync(string orgNr, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(Endpoint, new { organizationNumber = orgNr }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> ListAsync(CancellationToken ct)
    {
        var response = await _client.GetAsync(Endpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    [Fact]
    public async Task GET_company_watches_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync(Endpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_follow_returns_201_with_id_and_appears_in_list()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var id = await FollowAsync(LegalOrgNr, ct);
        id.ShouldNotBeNullOrEmpty();

        var list = await ListAsync(ct);
        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("organizationNumber").GetString().ShouldBe(LegalOrgNr);
        item.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task POST_follow_invalid_org_number_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { organizationNumber = "nope" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_follow_twice_is_idempotent_same_id_single_row()
    {
        // Proves the value-converted OrganizationNumber equality translates to SQL — the second
        // follow must FIND the first row by (user_id, organization_number).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var first = await FollowAsync(LegalOrgNr, ct);
        var second = await FollowAsync(LegalOrgNr, ct);

        second.ShouldBe(first);
        var list = await ListAsync(ct);
        list.EnumerateArray().Count(w => w.GetProperty("organizationNumber").GetString() == LegalOrgNr)
            .ShouldBe(1);
    }

    [Fact]
    public async Task DELETE_unfollow_removes_from_list_and_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await FollowAsync(LegalOrgNr, ct);

        var first = await _client.DeleteAsync($"{Endpoint}/{id}", ct);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await ListAsync(ct);
        list.EnumerateArray().Any(w => w.GetProperty("id").GetString() == id).ShouldBeFalse();

        // Idempotent — a repeat unfollow of an owned, already-soft-deleted watch is still 204.
        var second = await _client.DeleteAsync($"{Endpoint}/{id}", ct);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DELETE_unfollow_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.DeleteAsync($"{Endpoint}/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_follow_after_unfollow_resurrects_single_row()
    {
        // FORK B1 — re-following a previously unfollowed org.nr resurrects the SAME row (proven by
        // the list showing exactly one entry for that org.nr after the cycle).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var firstId = await FollowAsync(LegalOrgNr, ct);
        (await _client.DeleteAsync($"{Endpoint}/{firstId}", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refollowedId = await FollowAsync(LegalOrgNr, ct);

        refollowedId.ShouldBe(firstId); // same physical row resurrected
        var list = await ListAsync(ct);
        list.EnumerateArray().Count(w => w.GetProperty("organizationNumber").GetString() == LegalOrgNr)
            .ShouldBe(1);
    }

    [Fact]
    public async Task GET_list_masks_sole_proprietor_personnummer_shaped_org_number()
    {
        // FORK C1 / D8(c) — a sole-prop (personnummer-shaped) org.nr is never surfaced raw.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await FollowAsync(SoleProprietorOrgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeTrue();
        item.GetProperty("organizationNumber").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GET_list_resolves_company_name_from_job_ads_for_followed_org_number()
    {
        // ADR 0087 D2 — company_name is resolved at READ from public job_ads (the STORED generated
        // organization_number column, `= ANY(...)` matched, then the DISTINCT name projection). Only
        // Postgres computes that column, so this is Testcontainers-only. Proves the positive branch
        // (name surfaced), not just the graceful-null branch the unit tests cover.
        var ct = TestContext.Current.CancellationToken;
        await SeedImportedJobAdAsync(LegalOrgNr, "Acme Bygg AB", ct);
        await AuthenticateAsync(ct);
        var id = await FollowAsync(LegalOrgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("companyName").GetString().ShouldBe("Acme Bygg AB");
        item.GetProperty("organizationNumber").GetString().ShouldBe(LegalOrgNr);
    }

    // ---- #455 — follow from a job ad + follow-state batch (Approach A: org.nr resolved server-side) ----

    private static string ByJobAd(Guid jobAdId) => $"{Endpoint}/by-job-ad/{jobAdId}";
    private const string StatusEndpoint = Endpoint + "/status";

    [Fact]
    public async Task POST_follow_by_job_ad_returns_201_and_appears_in_list_without_org_nr_on_wire()
    {
        // Proves the STORED organization_number column is resolved SERVER-SIDE (Postgres-computed, so
        // Testcontainers-only) and the raw org.nr never appears in the request/response of the follow.
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = await SeedImportedJobAdAsync(LegalOrgNr, "Acme Bygg AB", ct);
        await AuthenticateAsync(ct);

        var response = await _client.PostAsync(ByJobAd(jobAdId), content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var watchId = created.GetProperty("id").GetString()!;
        watchId.ShouldNotBeNullOrEmpty();

        // The follow landed a watch on the ad's org.nr (visible via the list's masked projection).
        var list = await ListAsync(ct);
        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == watchId);
        item.GetProperty("organizationNumber").GetString().ShouldBe(LegalOrgNr);
    }

    [Fact]
    public async Task POST_follow_by_job_ad_sole_proprietor_follows_but_never_surfaces_org_nr()
    {
        // ADR 0087 D8 — a sole-prop (personnummer-shaped) employer stays followable; the org.nr is
        // masked in the surfaced list (never leaves the server raw).
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = await SeedImportedJobAdAsync(SoleProprietorOrgNr, "Enskild Firma", ct);
        await AuthenticateAsync(ct);

        var response = await _client.PostAsync(ByJobAd(jobAdId), content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var watchId = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var list = await ListAsync(ct);
        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == watchId);
        item.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeTrue();
        item.GetProperty("organizationNumber").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_follow_by_job_ad_without_org_number_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = await SeedImportedJobAdWithoutOrgNrAsync("Namnlös AB", ct);
        await AuthenticateAsync(ct);

        var response = await _client.PostAsync(ByJobAd(jobAdId), content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_follow_by_job_ad_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsync(ByJobAd(Guid.NewGuid()), content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_follow_by_job_ad_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsync(ByJobAd(Guid.NewGuid()), content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_follow_by_job_ad_twice_is_idempotent_same_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = await SeedImportedJobAdAsync(LegalOrgNr, "Acme Bygg AB", ct);
        await AuthenticateAsync(ct);

        var firstId = (await (await _client.PostAsync(ByJobAd(jobAdId), null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString();
        var secondId = (await (await _client.PostAsync(ByJobAd(jobAdId), null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString();

        secondId.ShouldBe(firstId);
        var list = await ListAsync(ct);
        list.EnumerateArray().Count(w => w.GetProperty("organizationNumber").GetString() == LegalOrgNr)
            .ShouldBe(1);
    }

    [Fact]
    public async Task POST_status_reports_followed_followable_and_absent_org_nr()
    {
        var ct = TestContext.Current.CancellationToken;
        var followedAdId = await SeedImportedJobAdAsync(LegalOrgNr, "Acme Bygg AB", ct);
        var otherAdId = await SeedImportedJobAdAsync("5560360793", "Beta Data AB", ct);
        var noOrgAdId = await SeedImportedJobAdWithoutOrgNrAsync("Namnlös AB", ct);
        await AuthenticateAsync(ct);

        // Follow only the first ad's employer (by-job-ad), then ask for the batch status of all three.
        var followResp = await _client.PostAsync(ByJobAd(followedAdId), null, ct);
        var expectedWatchId = (await followResp.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("id").GetString();

        var statusResp = await _client.PostAsJsonAsync(
            StatusEndpoint, new { jobAdIds = new[] { followedAdId, otherAdId, noOrgAdId } }, ct);
        statusResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await statusResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var statuses = body.GetProperty("statuses").EnumerateArray().ToList();

        // No org.nr is ever present in the batch response (guard by construction).
        statuses.All(s => !s.TryGetProperty("organizationNumber", out _)).ShouldBeTrue(
            "the follow-state batch response must never carry an org.nr member (ADR 0087 D8(c)).");

        var followed = statuses.Single(s => s.GetProperty("jobAdId").GetGuid() == followedAdId);
        followed.GetProperty("followable").GetBoolean().ShouldBeTrue();
        followed.GetProperty("companyWatchId").GetString().ShouldBe(expectedWatchId);

        var other = statuses.Single(s => s.GetProperty("jobAdId").GetGuid() == otherAdId);
        other.GetProperty("followable").GetBoolean().ShouldBeTrue();
        other.GetProperty("companyWatchId").ValueKind.ShouldBe(JsonValueKind.Null);

        var noOrg = statuses.Single(s => s.GetProperty("jobAdId").GetGuid() == noOrgAdId);
        noOrg.GetProperty("followable").GetBoolean().ShouldBeFalse();
        noOrg.GetProperty("companyWatchId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_status_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync(
            StatusEndpoint, new { jobAdIds = new[] { Guid.NewGuid() } }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_status_over_cap_returns_400()
    {
        // The 100-id cap validator is wired into the pipeline for /status (parity ADR 0063).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        var response = await _client.PostAsJsonAsync(StatusEndpoint, new { jobAdIds = tooMany }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Imports a public job_ad whose raw_payload carries employer.organization_number, so the STORED
    // generated `organization_number` column auto-populates (mirrors ListJobAdsEmployerFilterTests).
    // Returns the ad's id so the #455 by-job-ad endpoint can target it.
    private async Task<Guid> SeedImportedJobAdAsync(string orgNr, string companyName, CancellationToken ct)
    {
        var employer = $"{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}";
        return await SeedImportedJobAdCoreAsync(companyName, employer, ct);
    }

    // #455 — a job_ad whose employer carries NO organization_number, so the STORED column is NULL
    // (the B2 not-re-ingested reality). The by-job-ad follow must reject it with 400.
    private async Task<Guid> SeedImportedJobAdWithoutOrgNrAsync(string companyName, CancellationToken ct)
    {
        var employer = $"{{\"name\":\"{companyName}\"}}";
        return await SeedImportedJobAdCoreAsync(companyName, employer, ct);
    }

    private async Task<Guid> SeedImportedJobAdCoreAsync(string companyName, string employerJson, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"cw-name-{Guid.NewGuid()}";
        var rawPayload = $"{{\"id\":\"{externalId}\",\"employer\":{employerJson}}}";

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }
}
