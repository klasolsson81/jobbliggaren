using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
// Handler-/validator-enhetstester lever i Application.UnitTests (PR #121) — dupliceras ej.
[Collection("Api")]
public class MatchPreferencesTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static object Body(
        string[]? occupationGroups = null,
        string[]? regions = null,
        string[]? employmentTypes = null,
        string[]? skills = null,
        int? experienceYears = null,
        object[]? occupationExperience = null,
        bool remote = false) => new
        {
            preferredOccupationGroups = occupationGroups,
            preferredRegions = regions,
            preferredEmploymentTypes = employmentTypes,
            preferredSkills = skills,
            experienceYears,
            preferredOccupationExperience = occupationExperience,
            preferredRemote = remote,
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

    // #551 punkt 4 — the WIRE contract for the distans axis, and this pin exists
    // because its absence shipped a defect. The FE profile schema was made to require
    // `preferredRemote` on the strength of a comment asserting the backend projected
    // it. It did not. Every unit suite stayed green — the FE fixtures had been updated
    // to match the assumption, which is a production fact asserted off a premise
    // production could not produce (§5 `Tests:`) — and only the observe-only Playwright
    // job, which blocks nothing, caught the parse failure.
    //
    // The axis is on the profile DTO for the same page-wipe reason as the lists: the
    // write is a full-replace PUT, so without the round-trip, saving any other
    // dimension sends preferredRemote: false and silently switches the user's Distans
    // preference off.
    [Fact]
    public async Task PUT_match_preferences_round_trips_preferredRemote_through_the_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            Body(occupationGroups: ["grp_12345"], remote: true),
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        profile.TryGetProperty("preferredRemote", out var remote)
            .ShouldBeTrue("profil-DTO:n MÅSTE bära preferredRemote — FE:s schema kräver en bool, "
                + "och ett saknat fält får varje profilläsning att fela i parsningen");
        remote.GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Profile_carries_preferredRemote_false_for_a_user_who_never_set_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Ingen PUT alls: default-fallet är det som varje ny användare möter, och det
        // är där ett utelämnat fält hade slagit hårdast.
        var profile = await GetProfileAsync(ct);
        profile.TryGetProperty("preferredRemote", out var remote)
            .ShouldBeTrue("profil-DTO:n MÅSTE bära preferredRemote även för en användare som "
                + "aldrig satt den — FE:s schema kräver en bool, och default-fallet är det varje "
                + "NY användare möter, alltså där ett saknat fält slår bredast");
        remote.GetBoolean().ShouldBeFalse();
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

    // ===============================================================
    // #1918 — PATCH /api/v1/me/match-preferences, the per-part write: five optional
    // part objects, a present part replaced whole, an absent one left alone. The bodies are raw
    // JSON because which parts and members are PRESENT is the contract under test.
    // ===============================================================

    private const string PatchPath = "/api/v1/me/match-preferences";

    private static readonly string[] MatchPreferenceKeys =
    [
        "preferredOccupationGroups",
        "preferredOccupationExperience",
        "preferredSkills",
        "preferredRegions",
        "preferredMunicipalities",
        "preferredRemote",
        "preferredEmploymentTypes",
        "experienceYears",
    ];

    // Every field away from its default, written through the endpoint under test in one request.
    private const string AllFiveParts = """
        {
          "occupations": {
            "preferredOccupationGroups": ["grp_a", "grp_b"],
            "preferredOccupationExperience": [{"conceptId": "grp_a", "years": 4}, {"conceptId": "grp_b", "years": 9}]
          },
          "skills": {"preferredSkills": ["sk_a", "sk_b"]},
          "locations": {"preferredRegions": ["reg_a"], "preferredMunicipalities": ["kn_a"], "preferredRemote": true},
          "employmentTypes": {"preferredEmploymentTypes": ["et_a"]},
          "experience": {"experienceYears": 7}
        }
        """;

    private static readonly Dictionary<string, string> Seeded = new()
    {
        ["preferredOccupationGroups"] = """["grp_a","grp_b"]""",
        ["preferredOccupationExperience"] = """[{"conceptId":"grp_a","years":4},{"conceptId":"grp_b","years":9}]""",
        ["preferredSkills"] = """["sk_a","sk_b"]""",
        ["preferredRegions"] = """["reg_a"]""",
        ["preferredMunicipalities"] = """["kn_a"]""",
        ["preferredRemote"] = "true",
        ["preferredEmploymentTypes"] = """["et_a"]""",
        ["experienceYears"] = "7",
    };

    private static readonly Dictionary<string, string> NothingWritten = new();

    private Task<HttpResponseMessage> PatchAsync(string json, CancellationToken ct) =>
        _client.PatchAsync(PatchPath, new StringContent(json, Encoding.UTF8, "application/json"), ct);

    private static string Canonical(JsonElement element) => JsonSerializer.Serialize(element);

    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    // The profile holds `written` for the keys it names and the seeded value for every other key.
    private async Task ShouldHoldAsync(Dictionary<string, string> written, CancellationToken ct)
    {
        var profile = await GetProfileAsync(ct);
        foreach (var key in MatchPreferenceKeys)
        {
            var expected = written.TryGetValue(key, out var value) ? value : Seeded[key];
            Canonical(profile.GetProperty(key)).ShouldBe(Canonical(expected), key);
        }
    }

    private async Task SeedAllFivePartsAsync(CancellationToken ct)
    {
        (await PatchAsync(AllFiveParts, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    private static (string Body, Dictionary<string, string> Written) OnePart(string part) => part switch
    {
        "occupations" => (
            """{"occupations":{"preferredOccupationGroups":["grp_c"],"preferredOccupationExperience":[{"conceptId":"grp_c","years":2}]}}""",
            new()
            {
                ["preferredOccupationGroups"] = """["grp_c"]""",
                ["preferredOccupationExperience"] = """[{"conceptId":"grp_c","years":2}]""",
            }),
        "skills" => (
            """{"skills":{"preferredSkills":["sk_z"]}}""",
            new() { ["preferredSkills"] = """["sk_z"]""" }),
        "locations" => (
            """{"locations":{"preferredRegions":["reg_z"],"preferredMunicipalities":["kn_z"],"preferredRemote":false}}""",
            new()
            {
                ["preferredRegions"] = """["reg_z"]""",
                ["preferredMunicipalities"] = """["kn_z"]""",
                ["preferredRemote"] = "false",
            }),
        "employmentTypes" => (
            """{"employmentTypes":{"preferredEmploymentTypes":["et_z"]}}""",
            new() { ["preferredEmploymentTypes"] = """["et_z"]""" }),
        "experience" => (
            """{"experience":{"experienceYears":12}}""",
            new() { ["experienceYears"] = "12" }),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

    [Fact]
    public async Task PATCH_match_preferences_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await PatchAsync("""{"skills":{"preferredSkills":["sk_z"]}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("occupations")]
    [InlineData("skills")]
    [InlineData("locations")]
    [InlineData("employmentTypes")]
    [InlineData("experience")]
    public async Task PATCH_match_preferences_with_one_part_returns_204_and_changes_only_that_part(string part)
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);
        var (body, written) = OnePart(part);

        var response = await PatchAsync(body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHoldAsync(written, ct);
    }

    [Fact]
    public async Task PATCH_match_preferences_with_an_empty_body_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync("{}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    [Fact]
    public async Task PATCH_match_preferences_with_a_flat_put_shaped_body_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """{"preferredOccupationGroups":["grp_c"],"preferredRegions":["reg_z"],"preferredEmploymentTypes":["et_z"],"preferredMunicipalities":["kn_z"],"preferredSkills":["sk_z"],"experienceYears":12,"preferredOccupationExperience":[],"preferredRemote":false}""",
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    // A half-migrated client that sends a valid part beside a flat, PUT-shaped member.
    [Fact]
    public async Task PATCH_match_preferences_with_a_part_beside_a_flat_member_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """{"skills":{"preferredSkills":["sk_z"]},"experienceYears":12}""",
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    // A missing bool would bind to false, which reads as "does not want remote".
    [Fact]
    public async Task PATCH_match_preferences_locations_without_preferredRemote_returns_400_and_keeps_remote()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """{"locations":{"preferredRegions":["reg_z"],"preferredMunicipalities":["kn_z"]}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    [Fact]
    public async Task PATCH_match_preferences_with_an_empty_experience_part_returns_400_and_keeps_the_years()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync("""{"experience":{}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    [Fact]
    public async Task PATCH_match_preferences_with_experienceYears_null_returns_204_and_clears_the_years()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync("""{"experience":{"experienceYears":null}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHoldAsync(new Dictionary<string, string> { ["experienceYears"] = "null" }, ct);
    }

    [Fact]
    public async Task PATCH_match_preferences_part_without_its_list_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync("""{"skills":{}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    // An explicit null passes [JsonRequired], because the member is present; [] stays the only way to clear.
    [Fact]
    public async Task PATCH_match_preferences_part_with_a_null_list_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync("""{"skills":{"preferredSkills":null}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldHoldAsync(NothingWritten, ct);
    }

    // Without a years list, the stored years stay for the groups still chosen and go for the one removed.
    [Fact]
    public async Task PATCH_match_preferences_occupations_without_years_keeps_the_years_of_the_remaining_groups()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """{"occupations":{"preferredOccupationGroups":["grp_a","grp_c"]}}""", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHoldAsync(
            new Dictionary<string, string>
            {
                ["preferredOccupationGroups"] = """["grp_a","grp_c"]""",
                ["preferredOccupationExperience"] = """[{"conceptId":"grp_a","years":4}]""",
            },
            ct);
    }

    // The setup rail's four parts, with no experience part, must keep the stated years.
    [Fact]
    public async Task PATCH_match_preferences_with_the_rails_four_parts_leaves_experienceYears_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """
            {
              "occupations": {"preferredOccupationGroups": ["grp_c"], "preferredOccupationExperience": [{"conceptId": "grp_c", "years": 2}]},
              "skills": {"preferredSkills": ["sk_z"]},
              "locations": {"preferredRegions": ["reg_z"], "preferredMunicipalities": ["kn_z"], "preferredRemote": false},
              "employmentTypes": {"preferredEmploymentTypes": ["et_z"]}
            }
            """,
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldHoldAsync(
            new Dictionary<string, string>
            {
                ["preferredOccupationGroups"] = """["grp_c"]""",
                ["preferredOccupationExperience"] = """[{"conceptId":"grp_c","years":2}]""",
                ["preferredSkills"] = """["sk_z"]""",
                ["preferredRegions"] = """["reg_z"]""",
                ["preferredMunicipalities"] = """["kn_z"]""",
                ["preferredRemote"] = "false",
                ["preferredEmploymentTypes"] = """["et_z"]""",
            },
            ct);
    }

    // The validator checks each years entry on its own and leaves the subset rule to the domain, so this
    // 400 is the handler's failure through ToProblemResult, and the valid skills part beside it lands
    // nowhere.
    [Fact]
    public async Task PATCH_match_preferences_with_an_invalid_part_among_two_returns_400_and_writes_neither()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """{"skills":{"preferredSkills":["sk_z"]},"occupations":{"preferredOccupationGroups":["grp_c"],"preferredOccupationExperience":[{"conceptId":"grp_not_chosen","years":3}]}}""",
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("title").GetString().ShouldBe("MatchPreferences.OrphanOccupationExperience");
        await ShouldHoldAsync(NothingWritten, ct);
    }

    // A malformed body binds a null years entry. The per-part write may drop or refuse it, never answer 500.
    [Fact]
    public async Task PATCH_match_preferences_with_a_null_years_entry_is_not_a_server_error()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SeedAllFivePartsAsync(ct);

        var response = await PatchAsync(
            """{"occupations":{"preferredOccupationGroups":["grp_a"],"preferredOccupationExperience":[null,{"conceptId":"grp_a","years":4}]}}""",
            ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NoContent, HttpStatusCode.BadRequest);
    }
}
