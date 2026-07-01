using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0087 D3 / ADR 0031 — cross-user isolation for CompanyWatch. The follow is keyed by UserId;
/// user B must not see or unfollow user A's watch. Expected: 404 (not 403) so the existence of
/// another user's data is not revealed (parity with SavedSearch cross-user isolation).
/// </summary>
[Collection("Api")]
public class CompanyWatchesCrossUserIsolationTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";
    private const string OrgNr = "5592804784";

    private async Task<HttpClient> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task<string> FollowAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(Endpoint, new { organizationNumber = OrgNr }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task User_B_DELETE_company_watch_owned_by_user_A_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("cw-iso-a", ct);
        var clientB = await RegisterUserAsync("cw-iso-b", ct);
        var idA = await FollowAsync(clientA, ct);

        var response = await clientB.DeleteAsync($"{Endpoint}/{idA}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_GET_list_does_not_include_user_A_watches()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("cw-iso-a", ct);
        var clientB = await RegisterUserAsync("cw-iso-b", ct);
        var idA = await FollowAsync(clientA, ct);

        var response = await clientB.GetAsync(Endpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        json.EnumerateArray().Any(w => w.GetProperty("id").GetString() == idA)
            .ShouldBeFalse("user B:s lista ska inte innehålla user A:s bevakning");
    }

    [Fact]
    public async Task User_A_watch_intact_after_user_B_attempted_unfollow()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("cw-iso-a", ct);
        var clientB = await RegisterUserAsync("cw-iso-b", ct);
        var idA = await FollowAsync(clientA, ct);

        await clientB.DeleteAsync($"{Endpoint}/{idA}", ct);

        // User A's watch is untouched.
        var aList = await clientA.GetAsync(Endpoint, ct);
        var aJson = await aList.Content.ReadFromJsonAsync<JsonElement>(ct);
        aJson.EnumerateArray().Any(w => w.GetProperty("id").GetString() == idA)
            .ShouldBeTrue("user A:s bevakning ska vara orörd efter user B:s misslyckade unfollow");
    }
}
