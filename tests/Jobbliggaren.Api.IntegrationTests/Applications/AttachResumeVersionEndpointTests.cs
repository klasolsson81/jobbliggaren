using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// Fas 4 STEG B / B3 — HTTP wiring for the F4-11 AttachResumeVersion command
// (POST /api/v1/applications/{id}/resume-version). The relational IDOR/soft-delete invariants
// are already proven by AttachResumeVersionHandlerIntegrationTests; this covers the endpoint
// surface: auth, the 204 mapping, and the NotFound→404 mapping (own app + unknown version).
[Collection("Api")]
public class AttachResumeVersionEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"attach-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task<string> CreateResumeMasterVersionIdAsync(CancellationToken ct)
    {
        var post = await _client.PostAsJsonAsync(
            "/api/v1/resumes", new { name = "Mitt CV", fullName = "Klas Olsson" }, ct);
        var resumeId = (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        var get = await _client.GetAsync($"/api/v1/resumes/{resumeId}", ct);
        var detail = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        return detail.GetProperty("versions")[0].GetProperty("id").GetString()!;
    }

    private async Task<string> CreateManualApplicationAsync(CancellationToken ct)
    {
        var post = await _client.PostAsJsonAsync(
            "/api/v1/applications",
            new
            {
                jobAdId = (Guid?)null,
                coverLetter = (string?)null,
                manual = new { title = "Backend-utvecklare", company = "Acme AB", url = (string?)null, expiresAt = (DateTimeOffset?)null },
            },
            ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task POST_resume_version_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{Guid.NewGuid()}/resume-version",
            new { resumeVersionId = Guid.NewGuid() }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_resume_version_unknown_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{Guid.NewGuid()}/resume-version",
            new { resumeVersionId = Guid.NewGuid() }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_resume_version_unknown_version_on_own_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var appId = await CreateManualApplicationAsync(ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{appId}/resume-version",
            new { resumeVersionId = Guid.NewGuid() }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_resume_version_owns_both_returns_204()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var versionId = await CreateResumeMasterVersionIdAsync(ct);
        var appId = await CreateManualApplicationAsync(ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{appId}/resume-version",
            new { resumeVersionId = Guid.Parse(versionId) }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The link is surfaced on the application detail.
        var detail = await _client.GetAsync($"/api/v1/applications/{appId}", ct);
        var json = await detail.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("resumeVersionId").GetString().ShouldBe(versionId);
    }

    [Fact]
    public async Task POST_resume_version_replace_while_non_terminal_returns_204_with_latest()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var versionA = await CreateResumeMasterVersionIdAsync(ct);
        var versionB = await CreateResumeMasterVersionIdAsync(ct);
        var appId = await CreateManualApplicationAsync(ct);

        (await _client.PostAsJsonAsync($"/api/v1/applications/{appId}/resume-version",
            new { resumeVersionId = Guid.Parse(versionA) }, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Replace is allowed while non-terminal — "the version used" is a single current fact.
        (await _client.PostAsJsonAsync($"/api/v1/applications/{appId}/resume-version",
            new { resumeVersionId = Guid.Parse(versionB) }, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await _client.GetAsync($"/api/v1/applications/{appId}", ct);
        var json = await detail.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("resumeVersionId").GetString().ShouldBe(versionB);
    }

    [Fact]
    public async Task POST_resume_version_on_terminal_application_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var versionId = await CreateResumeMasterVersionIdAsync(ct);
        var appId = await CreateManualApplicationAsync(ct);

        // Draft → Submitted → Withdrawn (terminal); the aggregate's CanAttachResumeVersion grind
        // then rejects → "Application.ResumeVersionAttachNotAllowed" → the endpoint's 400 branch.
        await TransitionAsync(appId, "Submitted", ct);
        await TransitionAsync(appId, "Withdrawn", ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{appId}/resume-version",
            new { resumeVersionId = Guid.Parse(versionId) }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task TransitionAsync(string appId, string targetStatus, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{appId}/transition", new { targetStatus }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
