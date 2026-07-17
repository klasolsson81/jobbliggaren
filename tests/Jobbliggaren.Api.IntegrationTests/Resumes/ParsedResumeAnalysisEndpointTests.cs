using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 STEG B / B2 — HTTP wiring for the F4-9/F4-10 analysis ports over the import→analyze
// round-trip: review (CvReviewDto) and render (PDF bytes). Proves the auth gate, the fail-loud
// ?profile= validation, the owner-scoped IDOR, and — for render — that the PDF is returned as a
// raw application/pdf body, not JSON. The improvements half was removed with the åtgärda-lager's
// deferral (CV-pivot 2026-07-16, ADR 0112). The deep engine/renderer logic + the pnr-redaction +
// encryption are already covered by F4-9/F4-10 + the Worker tests; this covers the endpoint
// surface (incl. the review transmit boundary).
[Collection("Api")]
public class ParsedResumeAnalysisEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"analysis-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"analysis-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(PdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.IsSuccessStatusCode.ShouldBeTrue();
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    // ---- Auth + validation ----

    [Fact]
    public async Task GET_review_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}/review?profile=Ats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_review_invalid_profile_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{id}/review?profile=Klingon", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_render_missing_profile_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{id}/render", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_review_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}/review?profile=Ats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Happy paths over the import→analyze round-trip ----

    [Fact]
    public async Task Import_then_GET_review_returns_200_with_rubric_and_verdicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}/review?profile=Ats", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("rubricVersion").GetString().ShouldNotBeNullOrEmpty();
        json.GetProperty("profile").GetString().ShouldBe("Ats");
        json.GetProperty("verdicts").ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetProperty("verdicts").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Import_then_GET_review_assesses_D9_from_file_size_and_NotAssesses_B2_without_geometry()
    {
        // Fas 4b PR-6b end-to-end wiring (with the layout_metrics migration applied): the import
        // runs ICvLayoutAnalyzer on the fake 8-byte "%PDF" — which is not a real PDF, so the
        // analyzer returns Failed with FileSizeBytes = 8. The persisted metrics then drive the
        // review: D9 (file size) assesses the tiny file → Pass, while B2 (page count) has NO
        // geometry → NotAssessed. This proves the analyzer→persist→review chain over real Postgres.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}/review?profile=Ats", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);

        VerdictOf(json, "D9").ShouldBe("Pass",
            "en 8-byte-fil är långt under storlekstaket → D9 Pass (filstorleken är känd även för en misslyckad geometri-parse).");
        VerdictOf(json, "B2").ShouldBe("NotAssessed",
            "en falsk PDF ger ingen sidgeometri → B2 NotAssessed (ärligt tak, aldrig fabricerad Pass/Fail).");
    }

    // Reads the `verdict` name for a given `criterionId` from the review's verdicts array.
    private static string VerdictOf(JsonElement review, string criterionId)
    {
        foreach (var verdict in review.GetProperty("verdicts").EnumerateArray())
        {
            if (verdict.GetProperty("criterionId").GetString() == criterionId)
                return verdict.GetProperty("verdict").GetString()!;
        }

        throw new InvalidOperationException($"Kriteriet {criterionId} saknas i granskningens verdicts.");
    }

    // GET /parsed/{id}/improvements tests were REMOVED with the åtgärda-lager's deferral
    // (CV-pivot 2026-07-16, ADR 0112, CTO-bind D8 Opt C) — the endpoint is gone. The
    // handler/engine unit tests stay: they guard the mothballed motor.

    [Fact]
    public async Task Import_then_GET_render_returns_200_pdf_bytes_not_json()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}/render?profile=Visual", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await get.Content.ReadAsByteArrayAsync(ct);
        bytes.Length.ShouldBeGreaterThan(0);
        // A real PDF body starts with "%PDF".
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("%PDF");
    }

    // ---- IDOR ----

    [Fact]
    public async Task GET_review_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/parsed/{idA}/review?profile=Ats", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Render returns raw PII bytes — the strongest reason to prove its owner-scoping directly.
    [Fact]
    public async Task GET_render_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/parsed/{idA}/render?profile=Visual", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
