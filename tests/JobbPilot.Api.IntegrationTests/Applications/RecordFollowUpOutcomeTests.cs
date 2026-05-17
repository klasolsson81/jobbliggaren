using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Applications;

/// <summary>
/// RecordFollowUpOutcome-endpoint:
/// POST /api/v1/applications/{id}/follow-ups/{followUpId}/outcome
///
/// Success returnerar 200 eller 204 (non-generic Result, paritet med
/// transition-endpoint). RÖD tills endpoint + command implementerats.
/// </summary>
[Collection("Api")]
public class RecordFollowUpOutcomeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly object CreateBody = new { jobAdId = (Guid?)null, coverLetter = (string?)null };

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<(string applicationId, string followUpId)> CreateApplicationWithFollowUpAsync(
        HttpClient client, CancellationToken ct)
    {
        var postResponse = await client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var applicationId = postJson.GetProperty("id").GetString()!;

        var followUpResponse = await client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups",
            new
            {
                channel = "Email",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                note = (string?)null
            },
            ct);
        followUpResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var followUpJson = await followUpResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var followUpId = followUpJson.GetProperty("id").GetString()!;

        return (applicationId, followUpId);
    }

    [Fact]
    public async Task POST_outcome_records_outcome_and_returns_success()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var (applicationId, followUpId) = await CreateApplicationWithFollowUpAsync(_client, ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/{followUpId}/outcome",
            new { outcome = "Responded" },
            ct);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Verifiera att utfallet faktiskt persistades.
        var getResponse = await _client.GetAsync($"/api/v1/applications/{applicationId}", ct);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var followUps = getJson.GetProperty("followUps");
        followUps.GetArrayLength().ShouldBe(1);
        followUps[0].GetProperty("outcome").GetString().ShouldBe("Responded");
    }

    [Fact]
    public async Task POST_outcome_twice_returns_400_on_second_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var (applicationId, followUpId) = await CreateApplicationWithFollowUpAsync(_client, ct);

        await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/{followUpId}/outcome",
            new { outcome = "Responded" },
            ct);

        var second = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/{followUpId}/outcome",
            new { outcome = "NoResponse" },
            ct);

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_outcome_with_invalid_outcome_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var (applicationId, followUpId) = await CreateApplicationWithFollowUpAsync(_client, ct);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/{followUpId}/outcome",
            new { outcome = "Replied" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_outcome_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{Guid.NewGuid()}/follow-ups/{Guid.NewGuid()}/outcome",
            new { outcome = "Responded" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_outcome_on_unknown_follow_up_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var postJson = await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var applicationId = postJson.GetProperty("id").GetString()!;

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/{Guid.NewGuid()}/outcome",
            new { outcome = "Responded" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
