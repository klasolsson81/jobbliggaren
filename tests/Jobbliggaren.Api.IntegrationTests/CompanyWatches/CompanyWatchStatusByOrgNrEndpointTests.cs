using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #560 PR-C (ADR 0087 D8(c)) — end-to-end against Testcontainers Postgres: the ORG.NR-keyed follow-state
/// batch that backs the <c>/foretag/sok</c> per-row "Bevaka" overlay. Proves the things InMemory cannot:
/// the owner-scoped read (<c>Where(w =&gt; w.UserId == userId)</c>) + the value-converted
/// <c>OrganizationNumber</c> materialisation translate to SQL (the org.nr correlation itself runs
/// in-memory over the loaded watches), the response is POSITIONAL (1:1 with the request order, never a
/// dedup), the response carries NO org.nr member, and user B never sees user A's follow.
/// </summary>
[Collection("Api")]
public class CompanyWatchStatusByOrgNrEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";
    private const string StatusEndpoint = Endpoint + "/status/by-org-nr";

    // Org.nrs unique to this file (the [Collection("Api")] Postgres is SHARED and never reset). Follow
    // state is owner-scoped and every test registers a FRESH user, so cross-test leakage is structurally
    // avoided, but unique values keep intent local. Third digit ≥ 2 → legal entity.
    private const string FollowedOrgNr = "5598010001";
    private const string OtherOrgNr = "5598010002";
    private const string ThirdOrgNr = "5598010003";

    private HttpClient NewClient() => factory.CreateClient();

    private static async Task AuthenticateAsync(HttpClient client, CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<string> FollowAsync(HttpClient client, string orgNr, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(Endpoint, new { organizationNumber = orgNr }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    private static async Task<List<JsonElement>> StatusAsync(
        HttpClient client, string[] orgNrs, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(StatusEndpoint, new { organizationNumbers = orgNrs }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("statuses").EnumerateArray().ToList();
    }

    [Fact]
    public async Task POST_status_by_org_nr_reports_followed_and_null_positionally_without_org_nr_on_wire()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        await AuthenticateAsync(client, ct);

        // Follow only the SECOND-requested org.nr, so a positional response is the only way the id lands
        // in slot 1 (not slot 0).
        var watchId = await FollowAsync(client, FollowedOrgNr, ct);

        var statuses = await StatusAsync(client, [OtherOrgNr, FollowedOrgNr, ThirdOrgNr], ct);

        // No org.nr is ever present in the batch response (guard by construction).
        statuses.All(s => !s.TryGetProperty("organizationNumber", out _)).ShouldBeTrue(
            "the follow-state batch response must never carry an org.nr member (ADR 0087 D8(c)).");

        statuses.Count.ShouldBe(3);
        statuses[0].GetProperty("companyWatchId").ValueKind.ShouldBe(JsonValueKind.Null);
        statuses[1].GetProperty("companyWatchId").GetString().ShouldBe(watchId);
        statuses[2].GetProperty("companyWatchId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_status_by_org_nr_is_owner_scoped_user_b_never_sees_user_a_follow()
    {
        var ct = TestContext.Current.CancellationToken;

        var userA = NewClient();
        await AuthenticateAsync(userA, ct);
        await FollowAsync(userA, FollowedOrgNr, ct);

        var userB = NewClient();
        await AuthenticateAsync(userB, ct);
        var statuses = await StatusAsync(userB, [FollowedOrgNr], ct);

        // B posts A's followed org.nr — owner-scoped read returns null (B follows nothing).
        statuses.Single().GetProperty("companyWatchId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_status_by_org_nr_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();

        var response = await client.PostAsJsonAsync(
            StatusEndpoint, new { organizationNumbers = new[] { FollowedOrgNr } }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_status_by_org_nr_over_cap_returns_400()
    {
        // The 100-org.nr cap validator is wired into the pipeline.
        var ct = TestContext.Current.CancellationToken;
        var client = NewClient();
        await AuthenticateAsync(client, ct);
        var tooMany = Enumerable.Range(0, 101)
            .Select(i => i.ToString(CultureInfo.InvariantCulture).PadLeft(10, '0')).ToArray();

        var response = await client.PostAsJsonAsync(
            StatusEndpoint, new { organizationNumbers = tooMany }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
