using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 STEG B / B1b — HTTP wiring + fail-closed IDOR for the GetParsedResume staging read
// (GET /api/v1/resumes/parsed/{id}). The artifact is imported through the B1a endpoint, so
// these tests also prove the import → staging-read round-trip through real Postgres (incl.
// the field-encryption decrypt-on-read of the CV-PII content).
[Collection("Api")]
public class GetParsedResumeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"parsed-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"parsed-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
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
    public async Task GET_parsed_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_parsed_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.GetAsync($"/api/v1/resumes/parsed/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_then_GET_parsed_returns_200_with_the_artifact()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var id = await ImportAsync(_client, ct);

        var get = await _client.GetAsync($"/api/v1/resumes/parsed/{id}", ct);

        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("id").GetString().ShouldBe(id);
        json.GetProperty("status").GetString().ShouldBe("PendingReview");
        json.GetProperty("sourceFileName").GetString().ShouldBe("cv.pdf");
        json.TryGetProperty("confidence", out _).ShouldBeTrue();
        // The encrypted Content shadow decrypted on read into a real object graph (a null
        // Content would have NRE'd the mapper → 500, not this 200): assert the structure.
        var content = json.GetProperty("content");
        content.GetProperty("contact").ValueKind.ShouldBe(JsonValueKind.Object);
        content.GetProperty("experiences").ValueKind.ShouldBe(JsonValueKind.Array);
        content.GetProperty("skills").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task GET_parsed_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var idA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync($"/api/v1/resumes/parsed/{idA}", ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
