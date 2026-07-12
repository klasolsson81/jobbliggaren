using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-8b 8b.3 (CTO-bind Q1 Variant B) — HTTP wiring for the ephemeral-preview endpoint
// (GET /resumes/{id}/render/preview?template=&accent=&font=&density=). The mallbyggare's "Uppdatera
// förhandsvisning". Imports + promotes a Resume, then renders it with UNSAVED template options.
// Proves auth (401), the fail-closed IDOR (cross-user → 404), the 200→raw-PDF mapping (not JSON),
// the fail-loud option validation (unknown / missing name → 400), and the never-persist posture
// (the persisted options are untouched). Mirrors ResumeRenderEndpointTests (the canonical sibling).
[Collection("Api")]
public class ResumeRenderPreviewEndpointTests(ApiFactory factory)
{
    private const string ValidQuery = "template=MorkPanel&accent=WineRed&font=Classic&density=Airy";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"preview-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"preview-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static object PromoteBody(string name = "Mitt CV") =>
        new
        {
            name,
            content = new
            {
                personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = (string?)null, location = "Stockholm" },
                experiences = new[]
                {
                    new
                    {
                        company = "Acme AB",
                        role = "Backend-utvecklare",
                        startDate = "2021-03-01",
                        endDate = (string?)null,
                        description = "Ledde ett team om 8.",
                    },
                },
                educations = Array.Empty<object>(),
                skills = new[] { new { name = "C#", yearsExperience = (int?)8 } },
                summary = "Erfaren backend-utvecklare.",
            },
        };

    private static async Task<string> ImportAndPromoteAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(PdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.StatusCode.ShouldBe(HttpStatusCode.Created);
        var parsedId = (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;

        var promote = await client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody(), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task GET_render_preview_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync(
            $"/api/v1/resumes/{Guid.NewGuid()}/render/preview?{ValidQuery}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_render_preview_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync(
            $"/api/v1/resumes/{Guid.NewGuid()}/render/preview?{ValidQuery}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Promote_then_GET_render_preview_returns_200_pdf_bytes_not_json()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var get = await _client.GetAsync(
            $"/api/v1/resumes/{resumeId}/render/preview?{ValidQuery}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await get.Content.ReadAsByteArrayAsync(ct);
        bytes.Length.ShouldBeGreaterThan(0);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("%PDF");
    }

    [Theory]
    [InlineData("template=NotATemplate&accent=WineRed&font=Classic&density=Airy")]
    [InlineData("template=MorkPanel&accent=WineRed&font=Classic")]  // missing density → NotEmpty fails
    public async Task GET_render_preview_invalid_or_missing_option_returns_400(string query)
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var response = await _client.GetAsync(
            $"/api/v1/resumes/{resumeId}/render/preview?{query}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // The preview returns raw PII bytes — prove owner-scoping directly over HTTP (parity /render).
    [Fact]
    public async Task GET_render_preview_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var resumeIdA = await ImportAndPromoteAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync(
            $"/api/v1/resumes/{resumeIdA}/render/preview?{ValidQuery}", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Never-persist (the core Q1 invariant): an ephemeral preview with options DIFFERENT from the
    // persisted ones must NOT mutate the stored template options. Promote (→ CvTemplateOptions.Default
    // = Klar/NavyBlue), preview a different set, then read the detail back and assert the persisted
    // options are untouched — the "preview == a read, never a write" posture proven over HTTP.
    [Fact]
    public async Task GET_render_preview_does_not_persist_the_ephemeral_options()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var before = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/resumes/{resumeId}", ct);
        before.GetProperty("templateOptions").GetProperty("template").GetString().ShouldBe("Klar");

        // ValidQuery = MorkPanel/WineRed/Classic/Airy — every member differs from the persisted default.
        var preview = await _client.GetAsync(
            $"/api/v1/resumes/{resumeId}/render/preview?{ValidQuery}", ct);
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/resumes/{resumeId}", ct);
        var opts = after.GetProperty("templateOptions");
        opts.GetProperty("template").GetString().ShouldBe("Klar");
        opts.GetProperty("accentColor").GetString().ShouldBe("NavyBlue");
    }
}
