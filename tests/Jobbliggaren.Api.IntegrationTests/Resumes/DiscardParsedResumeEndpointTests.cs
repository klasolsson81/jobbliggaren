using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-8.1 (#657, CTO-bind Q6) — HTTP wiring for DiscardParsedResume (the hub action
// card's "Ta bort utkastet"). POST /discard is a soft-delete state transition (parity
// /promote, never a DELETE); proves auth, the 204 happy path, that the artifact becomes
// invisible (global DeletedAt filter → 404 on both re-discard and detail read, and gone
// from latest-pending), and the fail-closed IDOR. Deep transition semantics are unit-tested
// on the handler; the audit row contract is pinned by the command's unit tests.
[Collection("Api")]
public class DiscardParsedResumeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"discard-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"discard-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
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

    [Fact]
    public async Task POST_discard_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsync(
            $"/api/v1/resumes/parsed/{Guid.NewGuid()}/discard", content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_discard_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.PostAsync(
            $"/api/v1/resumes/parsed/{Guid.NewGuid()}/discard", content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_then_discard_returns_204_and_artifact_becomes_invisible()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, ct);

        // The pending card sees the artifact before the discard...
        var pendingBefore = await _client.GetAsync("/api/v1/resumes/parsed/latest-pending", ct);
        (await pendingBefore.Content.ReadAsStringAsync(ct)).ShouldContain(parsedId);

        var discard = await _client.PostAsync(
            $"/api/v1/resumes/parsed/{parsedId}/discard", content: null, ct);
        discard.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // ...and after: soft-deleted → invisible everywhere (fail-closed 404, gone from
        // latest-pending), including a repeat discard (no state oracle for finalized
        // artifacts).
        var detail = await _client.GetAsync($"/api/v1/resumes/parsed/{parsedId}", ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var pendingAfter = await _client.GetAsync("/api/v1/resumes/parsed/latest-pending", ct);
        (await pendingAfter.Content.ReadAsStringAsync(ct)).ShouldNotContain(parsedId);

        var again = await _client.PostAsync(
            $"/api/v1/resumes/parsed/{parsedId}/discard", content: null, ct);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_discard_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var parsedIdA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var discardB = await clientB.PostAsync(
            $"/api/v1/resumes/parsed/{parsedIdA}/discard", content: null, ct);

        // IDOR fail-closed parity with /promote: indistinguishable from unknown.
        discardB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The artifact is untouched for its owner.
        var detailA = await clientA.GetAsync($"/api/v1/resumes/parsed/{parsedIdA}", ct);
        detailA.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
