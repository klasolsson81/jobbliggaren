using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-8.2 (#657, ADR 0093 §D5(e); CTO-bind Q3) — HTTP wiring for the canonical ATS-text
// query. Proves auth (401), fail-closed IDOR (404, no enumeration oracle), the "Linearized"
// source claim + non-empty text over the REAL decrypt + linearize pipeline, the private/no-store
// cache posture, and — the origin-independence claim of Q3 — that the view works for BOTH a
// promoted Import CV and a Template CV. The deep linearizer/redactor semantics are unit-tested;
// this is the end-to-end seam against real Postgres + the production field-encryption interceptors.
[Collection("Api")]
public class GetResumeAtsTextEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"ats-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"ats-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
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

    // Standard promote body (parity PromoteParsedResumeEndpointTests) — fullName "Anna Andersson".
    private static object PromoteBody(string name = "Mitt CV") =>
        new
        {
            name,
            content = new
            {
                personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = (string?)null, location = "Stockholm" },
                experiences = Array.Empty<object>(),
                educations = Array.Empty<object>(),
                skills = Array.Empty<object>(),
                summary = (string?)null,
            },
        };

    // Import → promote → the new canonical Resume id (the Import-origin arm).
    private static async Task<string> ImportAndPromoteAsync(HttpClient client, CancellationToken ct)
    {
        var parsedId = await ImportAsync(client, ct);
        var promote = await client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody("Promoterat CV"), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task GET_ats_text_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}/ats-text", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_ats_text_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}/ats-text", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // security-auditor PR-8.2 Minor: the no-store posture is set BEFORE the null
        // branch, so the 404 carries it too — pinned so a refactor that moves the header
        // under the null-check fails here rather than silently weakening the posture.
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task GET_ats_text_belonging_to_other_user_returns_404_and_owner_still_gets_200()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var resumeIdA = await ImportAndPromoteAsync(clientA, ct);

        // User B cannot read A's canonical ATS text (fail-closed IDOR, no enumeration oracle).
        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/{resumeIdA}/ats-text", ct);
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A still reads their own.
        var getA = await clientA.GetAsync($"/api/v1/resumes/{resumeIdA}/ats-text", ct);
        getA.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_ats_text_for_promoted_import_returns_200_linearized_text_and_no_store()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var response = await _client.GetAsync($"/api/v1/resumes/{resumeId}/ats-text", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Personal content is never cached (private, no-store).
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("source").GetString().ShouldBe("Linearized");
        var text = json.GetProperty("text").GetString();
        text.ShouldNotBeNullOrEmpty();
        // The promoted fullName is a user-authored span the linearizer emits verbatim (D8).
        text!.ShouldContain("Anna Andersson");
    }

    [Fact]
    public async Task GET_ats_text_for_template_resume_returns_200_with_fullName_in_text()
    {
        // Q3 origin-independence: the ATS-text view is the SAME claim over BOTH origins. A
        // Template CV (POST / "Börja från profilen") linearizes to at least its contact block,
        // so the fullName is present exactly as for the promoted Import path above.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var post = await _client.PostAsJsonAsync(
            "/api/v1/resumes", new { name = "Mall-CV", fullName = "Klas Olsson" }, ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var resumeId = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var response = await _client.GetAsync($"/api/v1/resumes/{resumeId}/ats-text", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("source").GetString().ShouldBe("Linearized");
        var text = json.GetProperty("text").GetString();
        text.ShouldNotBeNullOrEmpty();
        text!.ShouldContain("Klas Olsson");
    }
}
