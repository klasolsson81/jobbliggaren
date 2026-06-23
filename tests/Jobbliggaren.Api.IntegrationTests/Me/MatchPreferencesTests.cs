using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

// F4-12 PR-B (ADR 0076) — PUT /api/v1/me/match-preferences end-to-end mot
// Testcontainers Postgres. Endpoint-/integration-lagret: auth-gate (401),
// full-replace-semantik (PUT bär hela settet, mergar inte), all-empty är en
// giltig write (rensar preferenser → HasStatedDesiredOccupation false) och
// 400 ProblemDetails vid ogiltig concept-id (ej 500). Round-trip bevisas mot
// GET /api/v1/me/profile, vars DTO projicerar de tre listorna.
//
// JobSeeker-aggregatet skapas av RegisterCommandHandler vid registrering, så en
// authad user via RegisterAndGetSessionIdAsync HAR redan en JobSeeker. Handler-/
// validator-enhetstester lever i Application.UnitTests (PR #121) — dupliceras ej.
[Collection("Api")]
public class MatchPreferencesTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static object Body(
        string[]? occupationGroups = null,
        string[]? regions = null,
        string[]? employmentTypes = null,
        string[]? skills = null,
        int? experienceYears = null,
        object[]? occupationExperience = null) => new
        {
            preferredOccupationGroups = occupationGroups,
            preferredRegions = regions,
            preferredEmploymentTypes = employmentTypes,
            preferredSkills = skills,
            experienceYears,
            preferredOccupationExperience = occupationExperience,
        };

    private async Task<JsonElement> GetProfileAsync(CancellationToken ct)
    {
        var response = await _client.GetAsync("/api/v1/me/profile", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static string[] ReadStringArray(JsonElement json, string property) =>
        [.. json.GetProperty(property).EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public async Task PUT_match_preferences_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(occupationGroups: ["grp_12345"]),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_match_preferences_with_valid_set_returns_204_and_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_12345"],
                regions: ["stockholm_AB"],
                employmentTypes: ["et_fast"]),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        ReadStringArray(profile, "preferredOccupationGroups").ShouldBe(["grp_12345"]);
        ReadStringArray(profile, "preferredRegions").ShouldBe(["stockholm_AB"]);
        ReadStringArray(profile, "preferredEmploymentTypes").ShouldBe(["et_fast"]);
        profile.GetProperty("hasStatedDesiredOccupation").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task PUT_match_preferences_twice_full_replaces_not_merges()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var first = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_AAA"],
                regions: ["region_X"],
                employmentTypes: ["et_AAA"]),
            ct);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_BBB"],
                regions: ["region_Y"],
                employmentTypes: ["et_BBB"]),
            ct);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Full-replace: endast set Y kvar — set X ska INTE vara mergat in.
        var profile = await GetProfileAsync(ct);
        ReadStringArray(profile, "preferredOccupationGroups").ShouldBe(["grp_BBB"]);
        ReadStringArray(profile, "preferredRegions").ShouldBe(["region_Y"]);
        ReadStringArray(profile, "preferredEmploymentTypes").ShouldBe(["et_BBB"]);
    }

    [Fact]
    public async Task PUT_match_preferences_all_empty_clears_and_returns_204()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Sätt först något → bevisa sedan att all-empty rensar.
        var seed = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_12345"],
                regions: ["stockholm_AB"],
                employmentTypes: ["et_fast"]),
            ct);
        seed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var cleared = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: [],
                regions: [],
                employmentTypes: []),
            ct);
        cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        ReadStringArray(profile, "preferredOccupationGroups").ShouldBeEmpty();
        ReadStringArray(profile, "preferredRegions").ShouldBeEmpty();
        ReadStringArray(profile, "preferredEmploymentTypes").ShouldBeEmpty();
        profile.GetProperty("hasStatedDesiredOccupation").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task PUT_match_preferences_with_invalid_concept_id_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // "bad id!" bryter ^[A-Za-z0-9_-]{1,32}$ (blanksteg + '!') → validation-fail.
        // 400 ProblemDetails, INTE 500.
        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(occupationGroups: ["bad id!"]),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // STEG 3 (ADR 0079) — confirmed skills + stated experience round-trip end-to-end
    // through the PUT command and the GET profile DTO projection (the page-wipe guard).
    [Fact]
    public async Task PUT_match_preferences_with_skills_and_experience_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_12345"],
                skills: ["skill_java", "skill_spring"],
                experienceYears: 5),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        ReadStringArray(profile, "preferredSkills").ShouldBe(["skill_java", "skill_spring"]);
        profile.GetProperty("experienceYears").GetInt32().ShouldBe(5);
        ReadStringArray(profile, "preferredOccupationGroups").ShouldBe(["grp_12345"]);
    }

    // STEG 3 (ADR 0079) — experience can be omitted (null = not stated); the DTO
    // projects null and the round-trip preserves "not stated".
    [Fact]
    public async Task PUT_match_preferences_without_experience_projects_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(occupationGroups: ["grp_12345"]),
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        profile.GetProperty("experienceYears").ValueKind.ShouldBe(JsonValueKind.Null);
        ReadStringArray(profile, "preferredSkills").ShouldBeEmpty();
    }

    // STEG 3 (ADR 0079) — out-of-range experience is a 400 ProblemDetails, not 500.
    [Fact]
    public async Task PUT_match_preferences_with_out_of_range_experience_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(experienceYears: 999),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ADR 0079-amendment (exp-per-occ PR-3) — the per-occupation experience overlay binds from
    // the nested JSON array, persists to jsonb, and round-trips through the GET profile DTO
    // projection (the read-side page-wipe partner). A null-years entry preserves "not stated".
    [Fact]
    public async Task PUT_match_preferences_with_occupation_experience_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_12345", "grp_67890"],
                occupationExperience:
                [
                    new { conceptId = "grp_12345", years = (int?)5 },
                    new { conceptId = "grp_67890", years = (int?)null },
                ]),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        var overlay = profile.GetProperty("preferredOccupationExperience").EnumerateArray().ToList();
        overlay.Count.ShouldBe(2);

        var withYears = overlay.Single(e => e.GetProperty("conceptId").GetString() == "grp_12345");
        withYears.GetProperty("years").GetInt32().ShouldBe(5);

        var withoutYears = overlay.Single(e => e.GetProperty("conceptId").GetString() == "grp_67890");
        withoutYears.GetProperty("years").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ADR 0079-amendment — an overlay entry for a group NOT in preferredOccupationGroups is a
    // subset-invariant failure → 400 ProblemDetails (MatchPreferences.OrphanOccupationExperience),
    // not 500.
    [Fact]
    public async Task PUT_match_preferences_with_orphan_occupation_experience_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(
                occupationGroups: ["grp_12345"],
                occupationExperience: [new { conceptId = "grp_not_preferred", years = (int?)3 }]),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_match_preferences_over_cap_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // MaxConceptIds = 400 → 401 element överskrider per-list-taket.
        var overCap = Enumerable.Range(0, 401).Select(i => $"grp_{i}").ToArray();
        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(occupationGroups: overCap),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
