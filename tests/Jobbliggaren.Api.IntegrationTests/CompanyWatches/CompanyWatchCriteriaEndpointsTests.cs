using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
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

        // ...and physically gone, past the (retained-until-demolition) query filter.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.CompanyWatchCriteria.IgnoreQueryFilters()
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
