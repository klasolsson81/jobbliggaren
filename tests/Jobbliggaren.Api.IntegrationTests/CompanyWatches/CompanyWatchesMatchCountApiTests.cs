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
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #452 (ADR 0087 D5-tillägg) — end-to-end on the wired API: <c>GET /api/v1/me/company-watches</c>
/// surfaces <c>matchingAdCount</c> ("X matchande annonser") per watched employer, computed at READ
/// from the current user's Fast match profile against the employer's public Active ads. Extends the
/// #447 <see cref="CompanyWatchesTests"/> Api-integration surface.
/// <para>
/// The COUNT correctness (Fast≡Full ≥Good oracle, status/soft-delete exclusions, per-org.nr GROUP
/// keying) is pinned exhaustively by <see cref="CompanyWatchMatchCountTests"/>; this file proves the
/// WIRE contract — the JSON member is present, honestly null when the user has stated no occupation
/// (not-assessed), a real 0 when assessed-but-no-match, and the ≥Good count when the profile matches.
/// </para>
/// <para>
/// The [Collection("Api")] Postgres is SHARED and never reset, so every test seeds a PRIVATE org.nr
/// + a PRIVATE occupation-group concept-id (Guid-suffixed) no other test touches — deterministic
/// counts despite the shared DB (memory api_integration_shared_db_contamination).
/// </para>
/// </summary>
[Collection("Api")]
public class CompanyWatchesMatchCountApiTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";
    private const string PrefsEndpoint = "/api/v1/me/match-preferences";

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // Sets the authenticated user's Fast match preferences (full-replace PUT). occupation + region so
    // an ad tagged with (group, region) grades Good (one confirmed secondary; employment NotAssessed).
    private async Task SetPreferencesAsync(
        string occupationGroup, string region, CancellationToken ct)
    {
        var response = await _client.PutAsJsonAsync(
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

    private static string NewOrgNr() => $"55{Random.Shared.Next(10000000, 99999999)}";
    private static string NewGroup() => $"grp-cw452-{Guid.NewGuid():N}"[..24];
    private static string NewRegion() => $"reg-cw452-{Guid.NewGuid():N}"[..24];

    // Seeds a public Active ad for orgNr tagged with (occupationGroup, region) so it grades Good
    // (≥Good) for a profile stating that group + region. raw_payload carries the nested
    // employer.organization_number (org.nr shadow) + top-level occupation_group + nested
    // workplace_address.region_concept_id (grade shadows). Only Postgres computes these generated
    // columns → this is Testcontainers-only.
    private async Task SeedMatchingAdAsync(
        string orgNr, string occupationGroup, string region, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"cw452-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"Match AB\",\"organization_number\":\"{orgNr}\"}},"
            + $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}},"
            + $"\"workplace_address\":{{\"region_concept_id\":\"{region}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Matchande annons",
            company: Company.Create("Match AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task GET_list_matchingAdCount_is_null_when_user_has_not_stated_occupation()
    {
        // Not-assessed: a fresh user with no stated occupation → the handler's SSYK-gate returns
        // null (never a hard 0 — the FE renders the "state your occupations" nudge). The JSON member
        // is present and null (JsonValueKind.Null), not absent.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        await SeedMatchingAdAsync(orgNr, NewGroup(), NewRegion(), ct); // an ad exists, but no profile
        await AuthenticateAsync(ct);
        var id = await FollowAsync(orgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("matchingAdCount").ValueKind.ShouldBe(JsonValueKind.Null,
            "En användare utan angivet yrke ska få matchingAdCount = null (not-assessed) — aldrig " +
            "en hård 0 som skulle läsas som 'inga matchande annonser'.");
    }

    [Fact]
    public async Task GET_list_matchingAdCount_reflects_at_least_good_matches_for_stated_profile()
    {
        // Assessed + matching: a profile stating the ad's (group, region) → the ad grades Good
        // (≥Good) → matchingAdCount == 1. Seeds 2 matching Active ads for the watched org.nr.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        var group = NewGroup();
        var region = NewRegion();
        await SeedMatchingAdAsync(orgNr, group, region, ct);
        await SeedMatchingAdAsync(orgNr, group, region, ct);
        await AuthenticateAsync(ct);
        await SetPreferencesAsync(group, region, ct);
        var id = await FollowAsync(orgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("matchingAdCount").ValueKind.ShouldBe(JsonValueKind.Number,
            "En användare som angett yrke ska få en assessad siffra (aldrig null).");
        item.GetProperty("matchingAdCount").GetInt32().ShouldBe(2,
            "Båda de seeed:ade (group, region)-annonserna ska grada Good (≥Good) → matchingAdCount == 2.");
    }

    [Fact]
    public async Task GET_list_matchingAdCount_is_zero_when_assessed_but_employer_has_no_match()
    {
        // Assessed-but-no-match: the user states an occupation, but the watched employer has no ad
        // matching that profile at ≥Good → a real 0 (assessed), NOT null. Seeds an ad under a
        // DIFFERENT occupation group so the SSYK gate fails → no tag → 0.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        var statedGroup = NewGroup();
        var region = NewRegion();
        // The employer's ad is tagged with a DIFFERENT group → below ≥Good for the stated profile.
        await SeedMatchingAdAsync(orgNr, NewGroup(), region, ct);
        await AuthenticateAsync(ct);
        await SetPreferencesAsync(statedGroup, region, ct);
        var id = await FollowAsync(orgNr, ct);

        var list = await ListAsync(ct);

        var item = list.EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);
        item.GetProperty("matchingAdCount").ValueKind.ShouldBe(JsonValueKind.Number,
            "En användare som angett yrke ska få en assessad siffra (0), aldrig null.");
        item.GetProperty("matchingAdCount").GetInt32().ShouldBe(0,
            "Arbetsgivaren har ingen ≥Good-matchande annons för den angivna profilen → 0 (assessad), " +
            "inte null (not-assessed).");
    }
}
