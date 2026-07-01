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
/// #452 (ADR 0087 D5-tillägg / D8, GDPR) — CROSS-USER isolation of the hub "matchande annonser"-count.
/// Two users A and B watch the SAME employer (same org.nr) but with DIFFERENT Fast profiles: A's
/// profile matches the seeded public ads at ≥Good, B's does not. Each user's <c>matchingAdCount</c>
/// MUST reflect ONLY that user's own profile — the count reads public ads + the CURRENT user's own
/// profile (<c>BuildFullForSortAsync</c> is ICurrentUser-scoped, <c>CountPerUserByEmployerAsync</c>
/// takes no other user's id), so there is NO cross-user surface. This is the §12-adjacent GDPR proof
/// that broadening the read to a per-user match label did not open a cross-user leak.
/// <para>
/// Mirrors <see cref="CompanyWatchesCrossUserIsolationTests"/>: two registered users, Bearer session
/// auth, private org.nr + private concept-ids (the shared [Collection("Api")] Postgres is never reset).
/// </para>
/// </summary>
[Collection("Api")]
public class CompanyWatchesMatchCountCrossUserIsolationTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";
    private const string PrefsEndpoint = "/api/v1/me/match-preferences";

    private async Task<HttpClient> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task SetPreferencesAsync(
        HttpClient client, string occupationGroup, string region, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync(
            PrefsEndpoint,
            new
            {
                preferredOccupationGroups = new[] { occupationGroup },
                preferredRegions = new[] { region },
                preferredEmploymentTypes = Array.Empty<string>(),
                preferredSkills = Array.Empty<string>(),
            },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<string> FollowAsync(HttpClient client, string orgNr, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(Endpoint, new { organizationNumber = orgNr }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    private static async Task<int?> MatchingAdCountForAsync(HttpClient client, string watchId, CancellationToken ct)
    {
        var response = await client.GetAsync(Endpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == watchId);
        var prop = item.GetProperty("matchingAdCount");
        return prop.ValueKind == JsonValueKind.Null ? null : prop.GetInt32();
    }

    private static string NewOrgNr() => $"55{Random.Shared.Next(10000000, 99999999)}";
    private static string NewGroup() => $"grp-cwiso-{Guid.NewGuid():N}"[..24];
    private static string NewRegion() => $"reg-cwiso-{Guid.NewGuid():N}"[..24];

    private async Task SeedMatchingAdAsync(
        string orgNr, string occupationGroup, string region, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"cwiso-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"Delad AB\",\"organization_number\":\"{orgNr}\"}},"
            + $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}},"
            + $"\"workplace_address\":{{\"region_concept_id\":\"{region}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Matchande annons",
            company: Company.Create("Delad AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Two_users_watching_same_employer_each_see_their_own_matchingAdCount()
    {
        var ct = TestContext.Current.CancellationToken;

        var orgNr = NewOrgNr();
        var region = NewRegion();
        var groupThatMatchesA = NewGroup();
        var groupThatMatchesNeither = NewGroup(); // B states this → no ad matches B

        // Two public Active ads for the SHARED employer, tagged with A's occupation group + region.
        await SeedMatchingAdAsync(orgNr, groupThatMatchesA, region, ct);
        await SeedMatchingAdAsync(orgNr, groupThatMatchesA, region, ct);

        // User A: profile matches the seeded ads (group + region) → ≥Good → count 2.
        var clientA = await RegisterUserAsync("cw452-iso-a", ct);
        await SetPreferencesAsync(clientA, groupThatMatchesA, region, ct);
        var watchA = await FollowAsync(clientA, orgNr, ct);

        // User B: profile states a DIFFERENT occupation group → the same ads never gate in → count 0.
        var clientB = await RegisterUserAsync("cw452-iso-b", ct);
        await SetPreferencesAsync(clientB, groupThatMatchesNeither, region, ct);
        var watchB = await FollowAsync(clientB, orgNr, ct);

        var countA = await MatchingAdCountForAsync(clientA, watchA, ct);
        var countB = await MatchingAdCountForAsync(clientB, watchB, ct);

        countA.ShouldBe(2,
            "User A:s matchingAdCount ska spegla A:s EGNA profil (matchar de delade annonserna vid " +
            "≥Good) — 2.");
        countB.ShouldBe(0,
            "User B:s matchingAdCount ska spegla B:s EGNA profil (matchar INTE de delade annonserna) " +
            "— 0 (assessad), aldrig A:s 2. Counten läser bara publika annonser + den AKTUELLA " +
            "användarens egna profil (ingen cross-user-JOIN, ADR 0087 D8 / GDPR).");
    }
}
