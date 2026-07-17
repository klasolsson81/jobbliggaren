using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 onboarding (CTO Variant B 2026-06-21) — HTTP wiring + fail-closed IDOR for the
// occupation-proposals read (GET /api/v1/resumes/parsed/{id}/occupations). The artifact is
// imported through the B1a endpoint, so this is the real-Postgres oracle proving the value-
// converter PROJECTION of the occupation_proposals jsonb translates and returns a JSON array —
// WITHOUT materialising the aggregate (so the CV-PII shadows are never decrypted; the query is
// not IRequiresFieldEncryptionKey, yet the read succeeds = it never touched the DEK pipeline).
[Collection("Api")]
public class GetParsedResumeOccupationsEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"occ-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"occ-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static MultipartFormDataContent PdfForm()
    {
        var part = new ByteArrayContent(PdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { part, "file", "cv.pdf" } };
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        using var form = PdfForm();
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.IsSuccessStatusCode.ShouldBeTrue();
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    [Fact]
    public async Task GET_occupations_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}/occupations", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_occupations_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}/occupations", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_then_GET_occupations_returns_200_with_a_json_array()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}/occupations", ct);

        // 200 (not 500) proves the jsonb projection translated against real PG without
        // materialising the aggregate (which, with no warmed DEK, would have thrown on the
        // CV-PII shadows). The minimal PDF has no parseable experience, so the deterministic
        // deriver yields an empty array — the wiring + projection are what this asserts.
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task GET_occupations_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/parsed/{idA}/occupations", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
