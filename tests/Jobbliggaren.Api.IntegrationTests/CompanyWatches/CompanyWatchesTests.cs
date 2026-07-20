using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
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

    // #994 — org.nrs UNIQUE to the register-name-fallback tests (the [Collection("Api")] Postgres is
    // SHARED and never reset; nothing else seeds a job_ad OR register row for these). Third digit 9 →
    // legal-entity shaped, so never pnr-masked.
    private const string RegisterOnlyOrgNr = "5598000010";  // followed, register row, NO job_ad
    private const string RegisterAndAdOrgNr = "5598000028"; // followed, BOTH a job_ad and a register row
    private const string DeregisteredRegisterOrgNr = "5598000036"; // followed, DEREGISTERED register row

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

    // ---- #994 — register name-fallback (Testcontainers: the register replica + the job_ads STORED
    //      generated org.nr column both need real Postgres) ----------------------------------------

    [Fact]
    public async Task GET_list_resolves_company_name_from_register_when_followed_company_has_no_ad()
    {
        // #994 — a company followed from the register with ZERO job_ads has no job_ads name to
        // project; the register HAS the name. The list falls back to it (a second READ projection, no
        // snapshot), so the row shows the real name instead of the "namn är inte tillgängligt" copy.
        var ct = TestContext.Current.CancellationToken;
        await SeedRegisterCompanyAsync(RegisterOnlyOrgNr, "Registret Bygg AB", ct);
        await AuthenticateAsync(ct);
        var id = await FollowAsync(RegisterOnlyOrgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("companyName").GetString().ShouldBe("Registret Bygg AB");
        item.GetProperty("organizationNumber").GetString().ShouldBe(RegisterOnlyOrgNr);
    }

    [Fact]
    public async Task GET_list_prefers_the_job_ads_name_over_the_register_name()
    {
        // When a company has BOTH an active ad ("Aktuella Namnet AB") AND a register row ("Register
        // Namnet AB"), the list surfaces the JOB_ADS name. This pins the OUTCOME — job_ads is the
        // primary source — enforced JOINTLY by the missingRegisterOrgNrs filter (the register is never
        // even queried for an org.nr job_ads already named) and the coalesce, NOT by the coalesce order
        // alone: the two name maps are disjoint by construction, so swapping the ?? operands is an
        // equivalent mutant (test-writer 2026-07-20) — the filter, not the order, is the arbiter.
        var ct = TestContext.Current.CancellationToken;
        await SeedImportedJobAdAsync(RegisterAndAdOrgNr, "Aktuella Namnet AB", ct);
        await SeedRegisterCompanyAsync(RegisterAndAdOrgNr, "Register Namnet AB", ct);
        await AuthenticateAsync(ct);
        var id = await FollowAsync(RegisterAndAdOrgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("companyName").GetString().ShouldBe("Aktuella Namnet AB");
    }

    [Fact]
    public async Task GET_list_resolves_each_row_from_its_own_source_in_a_mixed_follow_set()
    {
        // Two employer watches in ONE response: one named by job_ads, one by the register. Pins per-row
        // source selection + correct dict keying — a keying bug where the register name bled onto the
        // job_ads-named row (or vice versa) would surface here where the single-follow tests can't.
        var ct = TestContext.Current.CancellationToken;
        const string adOnly = "5598000044";       // job_ads only (third digit 9 → legal entity)
        const string registerOnly = "5598000051";  // register only
        await SeedImportedJobAdAsync(adOnly, "Annons Endast AB", ct);
        await SeedRegisterCompanyAsync(registerOnly, "Register Endast AB", ct);
        await AuthenticateAsync(ct);
        var adId = await FollowAsync(adOnly, ct);
        var regId = await FollowAsync(registerOnly, ct);

        var list = await ListAsync(ct);

        list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == adId)
            .GetProperty("companyName").GetString().ShouldBe("Annons Endast AB");
        list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == regId)
            .GetProperty("companyName").GetString().ShouldBe("Register Endast AB");
    }

    [Fact]
    public async Task GET_list_resolves_the_name_of_a_followed_company_that_has_since_deregistered()
    {
        // The register retains deregistered rows exactly so company_watch history stays resolvable
        // (ADR 0091); the name port does NOT apply the Active-only gate the search/browse ports do
        // (DPIA M-D6 gates discoverability, not the name of a company the user already follows). A
        // followed company that later deregistered keeps a resolvable, public name. Pins the
        // deliberate no-status-filter decision (#994) — an added Active gate here would go red.
        var ct = TestContext.Current.CancellationToken;
        await SeedRegisterCompanyAsync(
            DeregisteredRegisterOrgNr, "Avregistrerad Firma AB", ct, status: "Deregistered");
        await AuthenticateAsync(ct);
        var id = await FollowAsync(DeregisteredRegisterOrgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("companyName").GetString().ShouldBe("Avregistrerad Firma AB");
    }

    // ---- F4b (#803) — the per-watch filter on the LIST read (the row's pre-fill + BC-9′ disclosure) ----

    // Hoisted (CA1861): request-body arrays reused across the filter round-trip calls. The two geo
    // lists are DISJOINT JobTech namespaces — a kommun id is never a län id.
    private static readonly string[] FilterKommun = ["gbg_kn"];
    private static readonly string[] FilterLan = ["skane_lan"];
    private static readonly string[] NoFilterIds = [];

    [Fact]
    public async Task GET_list_round_trips_a_set_filter_with_both_geo_axes_intact()
    {
        // The write path (PUT /{id}/filter) is proven in SetCompanyWatchFilterEndpointTests; what THIS
        // pins is the other half of the loop — that the list READ hands the filter back through the
        // jsonb converter with both DISJOINT axes still in their own namespaces. A län concept-id that
        // came back in `municipalities` (or an expanded län) would be re-sent by the editor on the next
        // save and quietly become a filter that matches nothing.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await FollowAsync(LegalOrgNr, ct);

        var put = await _client.PutAsJsonAsync(
            $"{Endpoint}/{id}/filter",
            new
            {
                municipalities = FilterKommun,
                regions = FilterLan,
                onlyMatched = true,
            },
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        var filter = item.GetProperty("filter");
        filter.ValueKind.ShouldNotBe(JsonValueKind.Null);
        filter.GetProperty("municipalities").EnumerateArray()
            .Select(m => m.GetString()).ShouldBe(["gbg_kn"]);
        filter.GetProperty("regions").EnumerateArray()
            .Select(r => r.GetString()).ShouldBe(["skane_lan"],
                "läns-axeln måste överleva hela vägen jsonb → DTO → wire, oexpanderad");
        filter.GetProperty("onlyMatched").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task GET_list_reports_null_filter_for_an_unfiltered_watch()
    {
        // Absence is the FE's ONLY "no filter" signal (it renders no disclosure). The member must be
        // PRESENT and null — never omitted (the FE schema requires the key, so an omission is contract
        // drift that would render every watch as unfiltered) and never an empty object (a second,
        // non-canonical representation of "no filter").
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await FollowAsync(LegalOrgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.TryGetProperty("filter", out var filter).ShouldBeTrue(
            "filter-nyckeln måste finnas på varje rad — FE:ns schema kräver den, och ett utelämnat " +
            "värde skulle rendera en FILTRERAD bevakning som ofiltrerad");
        filter.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GET_list_reports_null_filter_after_the_user_clears_it()
    {
        // The clear path, end to end: an all-empty selection clears the column to SQL NULL, and the
        // list must then report the watch as unfiltered again — otherwise the row would keep disclosing
        // a filter the user has removed.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await FollowAsync(LegalOrgNr, ct);
        (await _client.PutAsJsonAsync(
            $"{Endpoint}/{id}/filter",
            new { municipalities = FilterKommun, regions = NoFilterIds, onlyMatched = true },
            ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(ct)).EnumerateArray()
            .Single(w => w.GetProperty("id").GetString() == id)
            .GetProperty("filter").ValueKind.ShouldNotBe(JsonValueKind.Null, "förutsättning: filtret är satt");

        (await _client.PutAsJsonAsync(
            $"{Endpoint}/{id}/filter",
            new
            {
                municipalities = NoFilterIds,
                regions = NoFilterIds,
                onlyMatched = false,
            },
            ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("filter").ValueKind.ShouldBe(JsonValueKind.Null);
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

    // ---- #447 — "X aktiva annonser just nu" per followed company (derived count over public job_ads) ----

    // Org.nrs UNIQUE to the #447 count assertions. The [Collection("Api")] Postgres is SHARED and never
    // reset between tests, so the shared LegalOrgNr accumulates active ads across the file — a count
    // assertion MUST seed a private org.nr no other test touches to stay deterministic (memory
    // api_integration_shared_db_contamination). Third digit ≥ 2 → legal entity (unmasked); the sole-prop
    // one has third digit < 2 → personnummer-shaped (masked). OrganizationNumber.Create validates only
    // 10 digits (no Luhn), so these are valid follow keys.
    private const string CountEmployerLegal = "5544700447";   // followed: 2 active + 1 archived → 2
    private const string CountEmployerOther = "5544800447";   // different employer, 1 active (excluded)
    private const string CountEmployerArchivedOnly = "5544900447"; // 1 archived only → 0
    private const string CountEmployerSoleProp = "9012310447"; // third digit 1 → masked, 1 active → 1

    [Fact]
    public async Task GET_list_reports_active_ad_count_over_public_job_ads()
    {
        // #447 (ADR 0087 D2) — the active-ad count is a derived count over PUBLIC job_ads
        // (status='Active' AND organization_number). status='Active' IS the whole exclusion: JobAd has
        // no soft-delete axis and no query filter (#821). Only Postgres computes the STORED
        // organization_number column + translates the GROUP BY
        // count, so this is Testcontainers-only. Seeds 2 Active ads + 1 Archived ad for the followed
        // org.nr, plus 1 Active ad for a DIFFERENT org.nr → the count must be exactly 2 (Archived
        // excluded by the status filter, the other employer excluded by the org.nr filter).
        var ct = TestContext.Current.CancellationToken;
        await SeedImportedJobAdAsync(CountEmployerLegal, "Acme Bygg AB", ct);
        await SeedImportedJobAdAsync(CountEmployerLegal, "Acme Bygg AB", ct);
        await SeedImportedJobAdAsync(CountEmployerLegal, "Acme Bygg AB", ct, archived: true);
        await SeedImportedJobAdAsync(CountEmployerOther, "Beta Data AB", ct); // different employer, active
        await AuthenticateAsync(ct);
        var id = await FollowAsync(CountEmployerLegal, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("activeAdCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GET_list_reports_active_ad_count_even_when_org_number_is_masked()
    {
        // #447 + D8(c) — the count is PUBLIC data (over public ads), surfaced even for a sole-prop
        // whose personnummer-shaped org.nr is masked. Proves the count is independent of the mask.
        var ct = TestContext.Current.CancellationToken;
        await SeedImportedJobAdAsync(CountEmployerSoleProp, "Enskild Firma", ct);
        await AuthenticateAsync(ct);
        var id = await FollowAsync(CountEmployerSoleProp, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("isProtectedIdentity").GetBoolean().ShouldBeTrue();
        item.GetProperty("organizationNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("activeAdCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task GET_list_reports_zero_active_ad_count_when_employer_has_no_active_ad()
    {
        // #447 — a followed org.nr with no public ad (or only retracted/archived ones) reports 0, not
        // a null or a missing member. Here the employer has one ARCHIVED ad only → count 0.
        var ct = TestContext.Current.CancellationToken;
        await SeedImportedJobAdAsync(CountEmployerArchivedOnly, "Gamla Firman AB", ct, archived: true);
        await AuthenticateAsync(ct);
        var id = await FollowAsync(CountEmployerArchivedOnly, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("activeAdCount").GetInt32().ShouldBe(0);
    }

    // Imports a public job_ad whose raw_payload carries employer.organization_number, so the STORED
    // generated `organization_number` column auto-populates (mirrors ListJobAdsEmployerFilterTests).
    // Returns the ad's id so the #455 by-job-ad endpoint can target it. When <paramref name="archived"/>
    // is true the ad is Archived after import (#447 — proves the status='Active' count filter).
    private async Task<Guid> SeedImportedJobAdAsync(
        string orgNr, string companyName, CancellationToken ct, bool archived = false)
    {
        var employer = $"{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}";
        return await SeedImportedJobAdCoreAsync(companyName, employer, ct, archived);
    }

    // #455 — a job_ad whose employer carries NO organization_number, so the STORED column is NULL
    // (the B2 not-re-ingested reality). The by-job-ad follow must reject it with 400.
    private async Task<Guid> SeedImportedJobAdWithoutOrgNrAsync(string companyName, CancellationToken ct)
    {
        var employer = $"{{\"name\":\"{companyName}\"}}";
        return await SeedImportedJobAdCoreAsync(companyName, employer, ct);
    }

    private async Task<Guid> SeedImportedJobAdCoreAsync(
        string companyName, string employerJson, CancellationToken ct, bool archived = false)
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
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        if (archived)
            jobAd.Archive(clock);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    // #994 — seeds a row into the local SCB company_register replica (the read-model the watch list
    // falls back to for a followed 0-ad company). Raw SQL: the entity is Infrastructure-internal and
    // deliberately OFF IAppDbContext (DPIA C-D4/M-C5), so the test writes the row directly rather than
    // referencing the type. An empty sni_codes + a default seat kommun are enough for a name lookup;
    // status is written by NAME ("Active"/"Deregistered"), matching HasConversion<string>.
    private async Task SeedRegisterCompanyAsync(
        string orgNr, string name, CancellationToken ct, string status = "Active")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $$"""
            INSERT INTO company_register
              (organization_number, company_name, sate_kommun_code, sni_codes,
               reklamsparr, status, synced_at, created_at)
            VALUES
              ({{orgNr}}, {{name}}, '0180', '{}'::text[], false, {{status}}, now(), now())
            """,
            ct);
    }
}
