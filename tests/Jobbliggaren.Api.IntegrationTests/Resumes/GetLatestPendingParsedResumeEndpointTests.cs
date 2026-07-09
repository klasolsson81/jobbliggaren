using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 onboarding decouple (ADR 0079-amendment 2026-06-23, pending-card bind) — HTTP wiring +
// owner-scope for the latest-pending-CV summary read (GET /api/v1/resumes/parsed/latest-pending).
// The artifact is imported through the B1a endpoint, so this is the real-Postgres oracle proving
// the plaintext-metadata PROJECTION (id / source_file_name / created_at) translates and returns a
// JSON object WITHOUT materialising the aggregate (the CV-PII shadows are never decrypted; the query
// is not IRequiresFieldEncryptionKey, yet the read succeeds = it never touched the DEK pipeline).
[Collection("Api")]
public class GetLatestPendingParsedResumeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string Route = "/api/v1/resumes/parsed/latest-pending";
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"pending-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"pending-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
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

    [Fact]
    public async Task GET_latest_pending_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync(Route, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_latest_pending_with_no_pending_cv_returns_200_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await NewAuthedClientAsync(_factory, ct);

        var response = await client.GetAsync(Route, ct);

        // No pending CV is a normal state → 200 with a null body, not 404.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Import_then_GET_latest_pending_returns_200_with_the_summary()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await NewAuthedClientAsync(_factory, ct);
        var id = await ImportAsync(client, ct);

        var get = await client.GetAsync(Route, ct);

        // 200 with an object (not 500) proves the plaintext-metadata projection translated against
        // real PG without materialising the aggregate (which, with no warmed DEK, would have thrown
        // on the CV-PII shadows).
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        json.GetProperty("id").GetString().ShouldBe(id);
        json.GetProperty("sourceFileName").GetString().ShouldBe("cv.pdf");
        json.GetProperty("uploadedAt").ValueKind.ShouldBe(JsonValueKind.String);

        // Fas 4b PR-8.1 (CTO-bind Q5): a fresh import denormalizes the confirm-task
        // presence flags at import time, so the meter data rides the summary — an object
        // (all-false for this degraded stub parse), proving the jsonb VO both persisted
        // and projected against real PG. Pre-PR-8 rows would carry null instead.
        var gaps = json.GetProperty("gaps");
        gaps.ValueKind.ShouldBe(JsonValueKind.Object);
        gaps.GetProperty("hasFullName").ValueKind.ShouldBe(JsonValueKind.False);
    }

    [Fact]
    public async Task GET_latest_pending_is_owner_scoped()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        await ImportAsync(clientA, ct);

        // User B has no pending CV of their own → A's pending CV is invisible (200 + null).
        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync(Route, ct);

        getB.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await getB.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
