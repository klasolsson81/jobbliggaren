using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #560 PR-3 — end-to-end over <c>/api/v1/me/company-watch-criteria</c>: the CRUD round-trip, the
/// composed browse (page + magnitude), the reference tree's cache contract, the preview count, and
/// the IDOR posture (foreign id ≡ unknown id ≡ 404). Register rows are seeded through the
/// production upsert path so the <c>text[]</c> predicate is exercised as production writes it.
/// </summary>
[Collection("Api")]
public class CompanyWatchCriteriaEndpointsTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watch-criteria";

    // Unique per test class run — the Api collection shares one Postgres, so register rows are
    // namespaced by SNI code to avoid cross-test contamination (the shared-DB seed lesson).
    private const string SniIt = "62100";
    private const string KommunStockholm = "0180";

    private static readonly string[] SniItArray = [SniIt];
    private static readonly string[] KommunStockholmArray = [KommunStockholm];

    // Well-formed five digits, not in SNI 2025 — the existence-validator's target.
    private static readonly string[] UnknownSniArray = ["99998"];

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task<string> CreateAsync(CancellationToken ct, string? label = null)
    {
        var response = await _client.PostAsJsonAsync(Endpoint, new
        {
            criteria = new { sniCodes = SniItArray, municipalityCodes = KommunStockholmArray },
            label,
        }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Endpoints_without_auth_return_401()
    {
        var ct = TestContext.Current.CancellationToken;

        (await _client.GetAsync(Endpoint, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetAsync($"{Endpoint}/reference", ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync(Endpoint, new { }, ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetAsync($"{Endpoint}/{Guid.NewGuid()}/companies", ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_list_patch_delete_roundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var id = await CreateAsync(ct, label: "IT i Stockholm");

        // List carries the raw codes + label (display labels are FE-derived, Fork G6).
        var list = await _client.GetFromJsonAsync<JsonElement>(Endpoint, ct);
        var item = list.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        item.GetProperty("label").GetString().ShouldBe("IT i Stockholm");
        item.GetProperty("sniCodes").EnumerateArray().Single().GetString().ShouldBe(SniIt);
        item.GetProperty("municipalityCodes").EnumerateArray().Single().GetString()
            .ShouldBe(KommunStockholm);

        // PATCH: present Label renames; absent Criteria untouched.
        var patch = await _client.PatchAsJsonAsync(
            $"{Endpoint}/{id}", new { label = "Nya namnet" }, ct);
        patch.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var renamed = await _client.GetFromJsonAsync<JsonElement>(Endpoint, ct);
        renamed.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id)
            .GetProperty("label").GetString().ShouldBe("Nya namnet");

        // DELETE is HARD (C-D8/G1): 204, then a repeat delete is 404 — the row is GONE.
        (await _client.DeleteAsync($"{Endpoint}/{id}", ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"{Endpoint}/{id}", ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // ...and physically gone. There is no filter left to read past: the demolition this comment
        // once anticipated has happened (C-D8/G1), so an ordinary read IS the whole-table read.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.CompanyWatchCriteria
            .AnyAsync(c => c.Id == new Domain.CompanyWatches.CompanyWatchCriterionId(Guid.Parse(id)), ct))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Create_with_unknown_sni_code_returns_400_naming_the_code()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new
        {
            criteria = new
            {
                sniCodes = UnknownSniArray,
                municipalityCodes = KommunStockholmArray,
            },
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain("99998");
    }

    [Fact]
    public async Task Browse_own_criterion_returns_the_page_AND_the_magnitude()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedRegisterAsync(ct,
            ("5560000012", "Acme AB"),
            ("5560000020", "Beta AB"),
            ("5560000038", "Gamma AB"));

        var id = await CreateAsync(ct);

        var response = await _client.GetAsync($"{Endpoint}/{id}/companies?page=1&pageSize=2", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // The composed response: the PAGE (pagination quantities) and the MAGNITUDE (the honest
        // "N företag" number) arrive as separate members — the FE can never conflate them.
        var companies = body.GetProperty("companies");
        companies.GetProperty("items").GetArrayLength().ShouldBe(2);
        companies.GetProperty("totalCount").GetInt32().ShouldBe(3);

        var magnitude = body.GetProperty("magnitude");
        magnitude.GetProperty("magnitude").GetInt32().ShouldBe(3);
        magnitude.GetProperty("saturated").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Browse_foreign_criterion_is_the_identical_404()
    {
        var ct = TestContext.Current.CancellationToken;

        // User A creates a criterion...
        await AuthenticateAsync(ct);
        var theirId = await CreateAsync(ct);

        // ...user B probes it, plus an id that exists for nobody.
        var clientB = _factory.CreateClient();
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(clientB, ct: ct);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);

        var foreign = await clientB.GetAsync($"{Endpoint}/{theirId}/companies", ct);
        var unknown = await clientB.GetAsync($"{Endpoint}/{Guid.NewGuid()}/companies", ct);

        // IDOR posture (C-D10/ADR 0031): both are 404 — never 403, never distinguishable.
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reference_returns_the_tree_with_ETag_and_304_on_IfNoneMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var first = await _client.GetAsync($"{Endpoint}/reference", ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag.ShouldNotBeNull();
        first.Headers.CacheControl!.ToString().ShouldContain("private");

        var tree = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        tree.GetProperty("sni").GetArrayLength().ShouldBe(22);
        tree.GetProperty("lan").GetArrayLength().ShouldBe(21);
        tree.GetProperty("sniVersion").GetString().ShouldNotBeNullOrWhiteSpace();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/reference");
        request.Headers.IfNoneMatch.Add(
            new System.Net.Http.Headers.EntityTagHeaderValue(first.Headers.ETag!.Tag, isWeak: true));
        var second = await _client.SendAsync(request, ct);
        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task PreviewCount_counts_an_unsaved_criterion_and_400s_a_missing_axis()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedRegisterAsync(ct, ("5560000046", "Delta AB"));

        var ok = await _client.PostAsJsonAsync($"{Endpoint}/preview-count", new
        {
            criteria = new { sniCodes = SniItArray, municipalityCodes = KommunStockholmArray },
        }, ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("magnitude").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        var missingAxis = await _client.PostAsJsonAsync($"{Endpoint}/preview-count", new
        {
            criteria = new { sniCodes = SniItArray, municipalityCodes = Array.Empty<string>() },
        }, ct);
        missingAxis.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_blank_label_clears_it_and_an_absent_label_is_left_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateAsync(ct, label: "IT i Stockholm");

        // Present-but-BLANK label ("" — exactly what the FE sends from an emptied field) CLEARS it.
        // The three-state contract's clear-branch is proven ONLY at the wire here: the handler unit
        // test constructs the command directly, so nothing else proves that JSON "" binds to a
        // PRESENT-blank (→ clear) rather than an ABSENT (→ untouched) Label.
        var cleared = await _client.PatchAsJsonAsync($"{Endpoint}/{id}", new { label = "" }, ct);
        cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        LabelOf(await _client.GetFromJsonAsync<JsonElement>(Endpoint, ct), id).ShouldBeNull();

        // A PATCH that OMITS label (only criteria present) leaves the now-null label untouched — the
        // absent-vs-blank distinction must survive JSON binding, or "no change" silently becomes
        // "clear" (and vice versa). Criteria unchanged in value; the assertion is about the label.
        var criteriaOnly = await _client.PatchAsJsonAsync(
            $"{Endpoint}/{id}",
            new { criteria = new { sniCodes = SniItArray, municipalityCodes = KommunStockholmArray } },
            ct);
        criteriaOnly.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        LabelOf(await _client.GetFromJsonAsync<JsonElement>(Endpoint, ct), id).ShouldBeNull();
    }

    [Fact]
    public async Task Reference_serves_the_full_tree_when_the_IfNoneMatch_ETag_is_stale()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // The counterfactual the happy-path 304 test cannot supply: the 304 fast-path must fire ONLY
        // on an EXACT ETag match. A stale/wrong ETag (the shape a picker cached before the dataset
        // version changed) must get the FULL 200 body — a loose or inverted comparison would answer
        // "not modified" for data that IS different, and the picker would render a stale tree.
        var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/reference");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"stale-and-wrong\"", isWeak: true));
        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("sni").GetArrayLength().ShouldBe(22);
    }

    // Reads a criterion's label from the list response, tolerant of both null-serialized and
    // null-ignored JSON configs (a cleared label may arrive as `label: null` or be omitted entirely).
    private static string? LabelOf(JsonElement list, string id)
    {
        var item = list.EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        return item.TryGetProperty("label", out var label) && label.ValueKind != JsonValueKind.Null
            ? label.GetString()
            : null;
    }

    [Fact]
    public async Task Ad_browse_own_criterion_returns_the_page_AND_the_magnitude()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedRegisterAsync(ct,
            ("5560000012", "Acme AB"),
            ("5560000020", "Beta AB"));

        // Three Active ads at Acme, one at Beta, and one at an employer this criterion does NOT
        // match — the last is what makes the join's WHERE observable rather than assumed.
        await SeedAdsAsync(ct,
            ("5560000012", 3, JobAdStatus.Active),
            ("5560000020", 1, JobAdStatus.Active),
            ("5569999999", 4, JobAdStatus.Active));

        var id = await CreateAsync(ct);

        var response = await _client.GetAsync($"{Endpoint}/{id}/ads?page=1&pageSize=2", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Same composed shape as /companies: the PAGE (pagination quantities) and the MAGNITUDE (the
        // honest "N aktiva annonser") arrive as separate members, so the FE cannot conflate them.
        var ads = body.GetProperty("ads");
        ads.GetProperty("items").GetArrayLength().ShouldBe(2);
        ads.GetProperty("totalCount").GetInt32().ShouldBe(4);

        var magnitude = body.GetProperty("magnitude");
        magnitude.GetProperty("magnitude").GetInt32().ShouldBe(4);
        magnitude.GetProperty("saturated").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Ad_browse_excludes_archived_ads_and_unmatched_employers()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedRegisterAsync(ct, ("5560000012", "Acme AB"));

        // Two Active ads and two Archived ones at the SAME matched employer, plus ads at an employer
        // outside the criterion. Only the two Active ones at Acme may be counted.
        await SeedAdsAsync(ct,
            ("5560000012", 2, JobAdStatus.Active),
            ("5560000012", 2, JobAdStatus.Archived),
            ("5569999999", 5, JobAdStatus.Active));

        var id = await CreateAsync(ct);

        var count = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ad-count", ct);
        count.GetProperty("magnitude").GetInt32().ShouldBe(2);

        var body = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ads", ct);
        var items = body.GetProperty("ads").GetProperty("items");
        items.GetArrayLength().ShouldBe(2);
        foreach (var item in items.EnumerateArray())
            item.GetProperty("status").GetString().ShouldBe(JobAdStatus.Active.Value);
    }

    [Fact]
    public async Task Ad_browse_orders_newest_first_across_the_wire()
    {
        // The port publishes published_at DESC and the handler re-states it; this is the only test
        // that sees the order the FE actually receives, through both.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedRegisterAsync(ct, ("5560000012", "Acme AB"));
        await SeedAdsAsync(ct, ("5560000012", 5, JobAdStatus.Active));

        var id = await CreateAsync(ct);

        var body = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ads", ct);
        var published = body.GetProperty("ads").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("publishedAt").GetDateTimeOffset())
            .ToList();

        published.Count.ShouldBe(5);
        published.ShouldBe(published.OrderByDescending(p => p).ToList());
    }

    [Fact]
    public async Task Ad_endpoints_for_a_foreign_criterion_are_the_identical_404()
    {
        var ct = TestContext.Current.CancellationToken;

        await AuthenticateAsync(ct);
        var theirId = await CreateAsync(ct);

        var clientB = _factory.CreateClient();
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(clientB, ct: ct);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);

        var foreignAds = await clientB.GetAsync($"{Endpoint}/{theirId}/ads", ct);
        var unknownAds = await clientB.GetAsync($"{Endpoint}/{Guid.NewGuid()}/ads", ct);
        var foreignCount = await clientB.GetAsync($"{Endpoint}/{theirId}/ad-count", ct);
        var unknownCount = await clientB.GetAsync($"{Endpoint}/{Guid.NewGuid()}/ad-count", ct);

        // IDOR posture (C-D10/ADR 0031): 404 on both surfaces, for both causes, never 403 and never
        // distinguishable — otherwise the ad routes would become the existence oracle /companies is
        // careful not to be.
        foreignAds.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownAds.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignCount.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownCount.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ad_browse_rejects_a_page_beyond_the_bound()
    {
        // The validator's bound and the port's count cap are one knowledge piece: page 101 is a 400,
        // which is what makes "TotalPages never exceeds MaxPage" true rather than hopeful.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await CreateAsync(ct);

        (await _client.GetAsync($"{Endpoint}/{id}/ads?page=101", ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Seeds Active/Archived <c>job_ads</c> for given employers through the production ingest entry
    /// point (<c>JobAd.Import</c>, then the archive transition) — so the rows these assertions rest on
    /// are ones <c>src/</c> produces (CLAUDE.md §5 <c>Tests:</c>). Like
    /// <see cref="SeedRegisterAsync"/> it first clears its own slice of the shared table, because the
    /// Api collection shares one Postgres and a previous run's ads would inflate the counts.
    /// </summary>
    private async Task SeedAdsAsync(
        CancellationToken ct, params (string OrgNr, int Count, JobAdStatus Status)[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orgNrs = rows.Select(r => r.OrgNr).Distinct().ToArray();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM job_ads WHERE organization_number = ANY({0});", [orgNrs], ct);

        var published = new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
        var offset = 0;

        foreach (var (orgNr, count, status) in rows)
        {
            for (var i = 0; i < count; i++)
            {
                var externalId = $"ext-{Guid.NewGuid():N}";
                // The org.nr reaches the column through the FACETS, which is the ACL's own path —
                // not a hand-set property.
                var payload = $"{{\"id\":\"{externalId}\",\"employer\":{{\"organization_number\":\"{orgNr}\"}}}}";
                var import = JobAd.Import(
                    title: $"Roll {offset}",
                    company: Company.Create("Seedad AB").Value,
                    description: "beskrivning",
                    url: $"https://example.com/jobs/{externalId}",
                    external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
                    rawPayload: payload,
                    facets: TestFacets.From(organizationNumber: orgNr),
                    publishedAt: published.AddDays(-offset),
                    expiresAt: published.AddDays(60),
                    clock: new FixedClock(published.AddDays(-offset)),
                    declaredContacts: [],
                    extractTerms: TestKeywordExtraction.None);
                import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import måste lyckas ({import.Error?.Code})");

                if (status == JobAdStatus.Archived)
                    import.Value.Archive(new FixedClock(published));

                db.JobAds.Add(import.Value);
                offset++;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Test clock — the house form in this project (parity the Applications suites).</summary>
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>
    /// Seeds Active register companies matching the test criterion, through the production upsert
    /// path. The Api collection shares one Postgres — rows accumulate, so tests assert against
    /// their OWN org.nrs (and the magnitude/count assertions seed all rows they count).
    /// </summary>
    private async Task SeedRegisterAsync(CancellationToken ct, params (string OrgNr, string Name)[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Own the predicate's slice of the shared table: these tests count rows matching
        // (SniIt ∧ KommunStockholm), so stale rows from a previous run would inflate the counts.
        await db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM company_register WHERE sni_codes && ARRAY['{SniIt}']::text[];", ct);

        var entries = rows.Select(r => new ScbCompanyRegisterEntry
        {
            OrganizationNumber = r.OrgNr,
            Name = r.Name,
            SeatMunicipalityCode = KommunStockholm,
            SeatMunicipalityName = "Stockholm",
            SniCodes = [SniIt],
            HasAdvertisingBlock = false,
            ScbStatusRaw = "1",
            Status = CompanyRegisterStatus.Active,
        }).ToList();

        await new ScbCompanyRegisterStore(db).UpsertBatchAsync(
            entries, new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero), ct);
    }
}
