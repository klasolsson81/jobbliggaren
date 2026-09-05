using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #1656 (b) — end-to-end on the wired API: a smart watch's PERSONAL ad count, and the view it links
/// to.
///
/// <para>
/// <b>The oracle is that the number and its destination are the SAME set.</b> Klas's condition is
/// that the count is "klickbart till exakt de annonserna", and this surface has shipped the opposite
/// twice (#1407, #1471): a count computed one way beside a link that lands on another. So the
/// assertions tie <c>/ad-count</c>'s <c>matching.count</c> to <c>/ads?onlyMatching=true</c>'s
/// <c>totalCount</c> AND to the identity of the ads that come back — cardinality alone would pass
/// for two different sets of the same size.
/// </para>
///
/// <para>
/// Testcontainers-only, and not incidentally: the grade rides STORED generated shadow columns and
/// <c>GradeRankExpression</c>'s SQL, the register join is raw SQL over a table that is not on
/// <c>IAppDbContext</c>, and the ad order is Postgres's. InMemory hides all three.
/// </para>
///
/// <para>
/// The <c>[Collection("Api")]</c> Postgres is SHARED and never reset, so this class owns its own SNI
/// leaf (62900, distinct from <see cref="CompanyWatchCriteriaEndpointsTests"/>'s 62100) and clears
/// only that slice, plus a Guid-suffixed occupation group per test.
/// </para>
/// </summary>
[Collection("Api")]
public class CriterionMatchingAdCountApiTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watch-criteria";
    private const string PrefsEndpoint = "/api/v1/me/match-preferences";

    // This class's own slice of the shared register. 62900 is a real SNI leaf and is NOT the one the
    // sibling endpoint suite seeds, so neither class's slice-delete can empty the other's fixture.
    private const string SniOwn = "62900";
    private const string KommunGoteborg = "1480";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Count_equals_the_set_the_matching_view_paginates()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = NewGroup();
        var region = NewRegion();
        var orgNr = NewOrgNr();

        await SeedRegisterAsync(orgNr, ct);
        // Three ads that grade >= Good for the profile below, and two the same employer posted that
        // carry no occupation group at all (rank 0). A count over the criterion's ADS would say 5.
        await SeedAdsAsync(orgNr, matching: 3, unmatched: 2, group, region, ct);

        await AuthenticateAsync(ct);
        await SetPreferencesAsync(group, region, ct);
        var id = await CreateCriterionAsync(ct);

        var count = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ad-count", ct);

        // The criterion HAS five active ads; three of them match. Both numbers arrive together, and
        // reading one where the other is meant is the mistake the separate members exist to prevent.
        count.GetProperty("ads").GetProperty("magnitude").GetInt32().ShouldBe(5);
        count.GetProperty("matching").GetProperty("count").GetInt32().ShouldBe(3);
        count.GetProperty("matching").GetProperty("tooBroad").GetBoolean().ShouldBeFalse();

        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            $"{Endpoint}/{id}/ads?onlyMatching=true", ct);

        // The destination's total is the SAME number the detail page rendered.
        filtered.GetProperty("ads").GetProperty("totalCount").GetInt32().ShouldBe(3);
        filtered.GetProperty("matching").GetProperty("count").GetInt32().ShouldBe(3);

        // ...and the same ADS, not merely the same many. A set of three unmatched ads would satisfy
        // every cardinality assertion above.
        var titles = filtered.GetProperty("ads").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("title").GetString())
            .ToList();
        titles.Count.ShouldBe(3);
        titles.ShouldAllBe(t => t!.StartsWith("Matchande", StringComparison.Ordinal));

        // The unfiltered view still shows all five, so the filter is a real narrowing rather than a
        // fixture that only ever had three ads (test-writer V5 — a baseline the null case shares).
        var all = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ads", ct);
        all.GetProperty("ads").GetProperty("totalCount").GetInt32().ShouldBe(5);
        all.GetProperty("matching").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Count_is_null_without_a_stated_occupation_and_the_filter_stays_inert()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();

        await SeedRegisterAsync(orgNr, ct);
        await SeedAdsAsync(orgNr, matching: 0, unmatched: 4, NewGroup(), NewRegion(), ct);

        // Authenticated but with NO preferences set: matching is undefined for this user.
        await AuthenticateAsync(ct);
        var id = await CreateCriterionAsync(ct);

        var count = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ad-count", ct);

        // Present and null on the wire (JsonValueKind.Null), never absent and never 0. A 0 would
        // tell a user with no stated occupation that nothing matches them.
        count.GetProperty("matching").GetProperty("count").ValueKind.ShouldBe(JsonValueKind.Null);
        count.GetProperty("matching").GetProperty("tooBroad").GetBoolean().ShouldBeFalse();

        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            $"{Endpoint}/{id}/ads?onlyMatching=true", ct);

        // INERT, not empty: all four ads are delivered. An empty list here would assert that none of
        // them matches, which is exactly what an unassessable profile cannot establish.
        filtered.GetProperty("ads").GetProperty("totalCount").GetInt32().ShouldBe(4);
        filtered.GetProperty("matching").GetProperty("count").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Count_is_zero_when_the_profile_matches_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();

        await SeedRegisterAsync(orgNr, ct);
        await SeedAdsAsync(orgNr, matching: 0, unmatched: 3, NewGroup(), NewRegion(), ct);

        await AuthenticateAsync(ct);
        // A profile stating an occupation NO seeded ad carries.
        await SetPreferencesAsync(NewGroup(), NewRegion(), ct);
        var id = await CreateCriterionAsync(ct);

        var count = await _client.GetFromJsonAsync<JsonElement>($"{Endpoint}/{id}/ad-count", ct);

        // A real 0, and it is a DIFFERENT wire shape from the not-assessed arm above. Collapsing the
        // two is the whole hazard: one means "nothing matches you", the other "we did not measure".
        count.GetProperty("matching").GetProperty("count").GetInt32().ShouldBe(0);
        count.GetProperty("matching").GetProperty("tooBroad").GetBoolean().ShouldBeFalse();

        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            $"{Endpoint}/{id}/ads?onlyMatching=true", ct);
        filtered.GetProperty("ads").GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Too_broad_refuses_the_number_and_still_delivers_the_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = NewGroup();
        var region = NewRegion();
        var orgNr = NewOrgNr();

        await SeedRegisterAsync(orgNr, ct);
        await SeedAdsAsync(orgNr, matching: 2, unmatched: 1, group, region, ct);

        await AuthenticateAsync(ct);
        await SetPreferencesAsync(group, region, ct);
        var id = await CreateCriterionAsync(ct);

        // Seeding past MaxSetSize would cost thousands of rows on a shared database, so the bound is
        // lowered to the fixture instead: the refusal is a property of the port's LIMIT, and this
        // asserts the whole pipeline honours it. The production value is pinned where it is used
        // (GetMyMatchingAdCountForCriterionQueryHandlerTests).
        CriterionMatchingAdSet.MaxSetSize.ShouldBeGreaterThan(3);

        var withinBound = await _client.GetFromJsonAsync<JsonElement>(
            $"{Endpoint}/{id}/ad-count", ct);
        withinBound.GetProperty("matching").GetProperty("count").GetInt32().ShouldBe(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var port = new CompanyWatchBrowseQuery(db);
        var spec = CompanyWatchCriteriaSpec.FromTrusted([SniOwn], [KommunGoteborg]);

        // The port itself is where the refusal lives, and it refuses rather than handing back the
        // first two of three -- a prefix that would have been graded and counted as if complete.
        (await port.ListActiveAdIdsAsync(spec, maxSetSize: 2, ct)).ShouldBeNull();
        (await port.ListActiveAdIdsAsync(spec, maxSetSize: 3, ct))!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Another_users_criterion_is_404_on_both_ad_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedRegisterAsync(NewOrgNr(), ct);
        await AuthenticateAsync(ct);
        var theirId = await CreateCriterionAsync(ct);

        var other = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(other, ct: ct);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // Unknown and cross-user are the same answer on both routes, so neither is an existence
        // oracle -- and the matching send must not turn a 404 into a 200 with a null count.
        (await other.GetAsync($"{Endpoint}/{theirId}/ad-count", ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"{Endpoint}/{theirId}/ads?onlyMatching=true", ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Fixture ────────────────────────────────────────────────────────────────────────────────

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    /// <summary>Full-replace PUT: occupation + region so a matching ad grades Good.</summary>
    private async Task SetPreferencesAsync(string group, string region, CancellationToken ct)
    {
        var response = await _client.PutAsJsonAsync(
            PrefsEndpoint,
            new
            {
                preferredOccupationGroups = new[] { group },
                preferredRegions = new[] { region },
                preferredEmploymentTypes = Array.Empty<string>(),
                preferredSkills = Array.Empty<string>(),
            },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<string> CreateCriterionAsync(CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(Endpoint, new
        {
            criteria = new { sniCodes = new[] { SniOwn }, municipalityCodes = new[] { KommunGoteborg } },
            label = (string?)null,
        }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    private async Task SeedRegisterAsync(string orgNr, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Own this class's slice of the shared table. Stale rows from a previous run would join to
        // stale ads and inflate every count here.
        await db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM company_register WHERE sni_codes && ARRAY['{SniOwn}']::text[];", ct);

        await new ScbCompanyRegisterStore(db).UpsertBatchAsync(
            [
                new ScbCompanyRegisterEntry
                {
                    OrganizationNumber = orgNr,
                    Name = "Matchande AB",
                    SeatMunicipalityCode = KommunGoteborg,
                    SeatMunicipalityName = "Göteborg",
                    SniCodes = [SniOwn],
                    HasAdvertisingBlock = false,
                    ScbStatusRaw = "1",
                    Status = CompanyRegisterStatus.Active,
                },
            ],
            new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero), ct);
    }

    /// <summary>
    /// Seeds Active ads through the production ingest entry point (<c>JobAd.Import</c>). The org.nr
    /// and the grade shadows reach their columns the way production's ACL puts them there — parsed
    /// OUT of the payload — because those columns are STORED generated and a hand-set value would be
    /// a premise production cannot produce (CLAUDE.md §5 <c>Tests:</c>).
    /// </summary>
    private async Task SeedAdsAsync(
        string orgNr, int matching, int unmatched, string group, string region, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM job_ads WHERE organization_number = {0};", [orgNr], ct);

        var published = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
        var offset = 0;

        void Add(string title, string? payloadGrade)
        {
            var externalId = $"m1656-{Guid.NewGuid():N}";
            var rawPayload =
                $"{{\"id\":\"{externalId}\","
                + $"\"employer\":{{\"name\":\"Matchande AB\",\"organization_number\":\"{orgNr}\"}}"
                + (payloadGrade ?? string.Empty)
                + "}";

            var import = JobAd.Import(
                title: title,
                company: Company.Create("Matchande AB").Value,
                description: "beskrivning",
                url: $"https://example.com/jobs/{externalId}",
                external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
                rawPayload: rawPayload,
                facets: TestFacets.FromPayload(rawPayload),
                publishedAt: published.AddDays(-offset),
                expiresAt: published.AddDays(60),
                clock: new FixedClock(published.AddDays(-offset)),
                declaredContacts: [],
                extractTerms: TestKeywordExtraction.None);
            import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import måste lyckas ({import.Error?.Code})");
            db.JobAds.Add(import.Value);
            offset++;
        }

        var graded =
            $",\"occupation_group\":{{\"concept_id\":\"{group}\"}}"
            + $",\"workplace_address\":{{\"region_concept_id\":\"{region}\"}}";

        for (var i = 0; i < matching; i++)
            Add($"Matchande annons {i}", graded);

        // No occupation group at all → rank 0 → never >= Good, whatever the profile says.
        for (var i = 0; i < unmatched; i++)
            Add($"Otaggad annons {i}", payloadGrade: null);

        await db.SaveChangesAsync(ct);
    }

    private static string NewOrgNr() => $"55{Random.Shared.Next(10000000, 99999999)}";
    private static string NewGroup() => $"grp-c1656-{Guid.NewGuid():N}"[..24];
    private static string NewRegion() => $"reg-c1656-{Guid.NewGuid():N}"[..24];

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
