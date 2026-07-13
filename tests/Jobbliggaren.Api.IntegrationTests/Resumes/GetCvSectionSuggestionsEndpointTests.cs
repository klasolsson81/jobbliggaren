using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b 8b.4a (ADR 0107) — HTTP wiring + fail-closed IDOR for the occupation-driven
// section-suggestion read (GET /api/v1/resumes/parsed/{id}/section-suggestions).
//
// This is the real-Postgres oracle for two things the InMemory handler tests cannot prove:
//
// (1) THE DEK PATH. The handler reads ParsedResume.Content (Form B, parsed_content_enc) to
//     answer "does she already HAVE this section?". The query is IRequiresFieldEncryptionKey,
//     so the prefetch behavior must warm the owner DEK before materialisation. In the unit
//     tests that path is a fake hydration interceptor; here it is the real envelope. A 200
//     (rather than a 500) is the proof.
//
// (2) THE TAXONOMY READ-MODEL against the seeded snapshot — GetContainingOccupationFieldsAsync
//     resolves the confirmed ssyk-4 groups to their parent occupation-fields off real rows,
//     not a dictionary a test wrote.
[Collection("Api")]
public class GetCvSectionSuggestionsEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"sect-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"sect-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(PdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };

        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    private static string Url(string id) => $"/api/v1/resumes/parsed/{id}/section-suggestions";

    [Fact]
    public async Task GET_section_suggestions_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync(Url(Guid.NewGuid().ToString()), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_section_suggestions_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(Url(Guid.NewGuid().ToString()), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_section_suggestions_belonging_to_other_user_returns_404()
    {
        // Fail-closed IDOR: the only real attack surface on a Guid-keyed read. 404, not 403 —
        // no enumeration oracle.
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync(Url(idA), ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_then_GET_section_suggestions_returns_the_honest_no_occupation_state()
    {
        // A freshly-registered user has stated NO occupation. That is empty state (1): she gets
        // the Övriga row's suggestions AND hasOccupationPreference:false, which is what tells the
        // guide to ask her for an occupation. The distinction from empty state (2) — a user whose
        // STATED occupation lands in Övriga — is the one thing this feature must not blur, so it
        // is asserted here on the wire, not only in a unit test.
        //
        // A 200 rather than a 500 is also the DEK proof: the handler dereferenced the decrypted
        // Form-B content behind the real envelope, with the owner key warmed by the prefetch
        // behavior.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync(Url(id), ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);

        json.GetProperty("branschgrupp").GetString().ShouldBe("ovriga");
        json.GetProperty("hasOccupationPreference").GetBoolean().ShouldBeFalse();

        // Övriga is a FIRST-CLASS row, not a hole — it carries its own suggestions and its own
        // rationale. A silently-empty payload here would be the feature looking alive and being
        // dead for 62 % of users.
        json.GetProperty("rationale").GetString().ShouldNotBeNullOrWhiteSpace();
        var suggestions = json.GetProperty("suggestions");
        suggestions.ValueKind.ShouldBe(JsonValueKind.Array);
        suggestions.GetArrayLength().ShouldBeGreaterThan(0);

        // Every suggestion carries the canonical id AND the heading that will be written into the
        // CV. The heading is never empty: an empty heading would produce an unparseable section.
        foreach (var suggestion in suggestions.EnumerateArray())
        {
            suggestion.GetProperty("sectionId").GetString().ShouldNotBeNullOrWhiteSpace();
            suggestion.GetProperty("heading").GetString().ShouldNotBeNullOrWhiteSpace();
            suggestion.TryGetProperty("isStandard", out var isStandard).ShouldBeTrue();
            isStandard.ValueKind.ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
        }

        // Klas ruling: "Referenser" is never OFFERED (it stays recognised — the file always wins).
        suggestions.EnumerateArray()
            .Select(s => s.GetProperty("sectionId").GetString())
            .ShouldNotContain("referenser");
    }

    [Fact]
    public async Task GET_section_suggestions_reflects_the_confirmed_occupation_from_match_preferences()
    {
        // The whole chain, over HTTP, against real Postgres: the user CONFIRMS a vård occupation
        // in her match preferences -> the taxonomy read-model resolves that ssyk-4 group to its
        // parent occupation-field off the SEEDED snapshot -> the branschgrupp asset maps that
        // field to "vard" -> the vård rule-table is returned.
        //
        // This is the test that would go red if someone re-pointed the slice at the CV's
        // UNCONFIRMED OccupationProposals (ADR 0040): the imported PDF carries no occupation at
        // all, so the proposals are empty, and the only thing that can produce "vard" here is the
        // confirmed axis.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        // A real ssyk-4 occupation-group under Hälso- och sjukvård (NYW6_mP6_vwf), read from the
        // seeded taxonomy tree rather than hardcoded — so the test cannot go stale on a snapshot
        // bump.
        var tree = await _client.GetFromJsonAsync<JsonElement>("/api/v1/job-ads/taxonomy", ct);
        var vardField = tree.GetProperty("occupationFields")
            .EnumerateArray()
            .First(f => f.GetProperty("conceptId").GetString() == "NYW6_mP6_vwf");
        var vardGroup = vardField.GetProperty("occupationGroups")
            .EnumerateArray()
            .First()
            .GetProperty("conceptId").GetString()!;

        var setPrefs = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            new
            {
                preferredOccupationGroups = new[] { vardGroup },
                preferredRegions = Array.Empty<string>(),
                preferredEmploymentTypes = Array.Empty<string>(),
            },
            ct);
        setPrefs.IsSuccessStatusCode.ShouldBeTrue(
            $"kunde inte sätta matchningsinställningar: {setPrefs.StatusCode}");

        var get = await _client.GetAsync(Url(id), ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);

        json.GetProperty("branschgrupp").GetString().ShouldBe("vard");
        // She HAS stated an occupation now — the guide must not ask her again.
        json.GetProperty("hasOccupationPreference").GetBoolean().ShouldBeTrue();
        json.GetProperty("rationale").GetString().ShouldBe("Vanligt inom vård och omsorg");

        var sectionIds = json.GetProperty("suggestions")
            .EnumerateArray()
            .Select(s => s.GetProperty("sectionId").GetString())
            .ToList();

        // The product promise of 8b.4a: an undersköterska is offered Legitimation och intyg …
        sectionIds.ShouldContain("legitimation");
        // … and is NOT offered Projekt (design handoff §7, vård row).
        sectionIds.ShouldNotContain("projekt");
    }
}
