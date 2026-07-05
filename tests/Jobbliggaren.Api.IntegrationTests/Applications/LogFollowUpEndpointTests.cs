using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// LogFollowUp-endpoint (ADR 0092 D4/D5):
/// POST /api/v1/applications/{id}/follow-ups/log
///
/// Loggar en genomförd kontakt idag (note valfri). Success → 201 Created; den
/// loggade follow-up:en dyker upp i GetApplicationById med outcome "Logged".
/// </summary>
[Collection("Api")]
public class LogFollowUpEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly object CreateBody = new { jobAdId = (Guid?)null, coverLetter = (string?)null };

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task<string> CreateApplicationAsync(CancellationToken ct)
    {
        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        return postJson.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task POST_log_creates_logged_follow_up_and_returns_201()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var applicationId = await CreateApplicationAsync(ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/log",
            new { note = "Ringde rekryteraren" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The logged follow-up appears in GetApplicationById with outcome "Logged".
        var getResponse = await _client.GetAsync($"/api/v1/applications/{applicationId}", ct);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var followUps = getJson.GetProperty("followUps");
        followUps.GetArrayLength().ShouldBe(1);
        followUps[0].GetProperty("outcome").GetString().ShouldBe("Logged");
        followUps[0].GetProperty("channel").GetString().ShouldBe("Other");
    }

    [Fact]
    public async Task POST_log_with_null_note_returns_201()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var applicationId = await CreateApplicationAsync(ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/log",
            new { note = (string?)null },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_log_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{Guid.NewGuid()}/follow-ups/log",
            new { note = "Kontakt" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_log_on_unknown_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{Guid.NewGuid()}/follow-ups/log",
            new { note = "Kontakt" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
