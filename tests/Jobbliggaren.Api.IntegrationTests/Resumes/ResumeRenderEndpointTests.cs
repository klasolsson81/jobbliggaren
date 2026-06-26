using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// TD-112 / #202 — HTTP wiring for the render-by-Resume-id endpoint (GET /resumes/{id}/render).
// Imports an artifact, promotes it into a canonical Resume, then renders that Resume by id. Proves
// auth, the 200→raw-PDF-bytes mapping (not JSON), the fail-loud profile validation (→ 400), and the
// fail-closed IDOR (cross-user → 404). The render returns raw decrypted PII bytes, so the
// owner-scoping is proven directly over HTTP. Mirrors ParsedResumeAnalysisEndpointTests (the
// parsed-render sibling) + PromoteParsedResumeEndpointTests (the promote helper).
[Collection("Api")]
public class ResumeRenderEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"render-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"render-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
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
    public async Task GET_render_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}/render?profile=Ats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_render_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/{Guid.NewGuid()}/render?profile=Ats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Promote_then_GET_render_returns_200_pdf_bytes_not_json()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/{resumeId}/render?profile=Visual", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await get.Content.ReadAsByteArrayAsync(ct);
        bytes.Length.ShouldBeGreaterThan(0);
        // A real PDF body starts with "%PDF".
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("%PDF");
    }

    [Fact]
    public async Task GET_render_invalid_profile_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var response = await _client.GetAsync($"/api/v1/resumes/{resumeId}/render?profile=Klingon", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Render returns raw PII bytes — the strongest reason to prove its owner-scoping directly.
    [Fact]
    public async Task GET_render_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var resumeIdA = await ImportAndPromoteAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/{resumeIdA}/render?profile=Visual", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
