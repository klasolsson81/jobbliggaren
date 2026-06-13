using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

[Collection("Api")]
public class CreateJobAdTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task POST_job_ad_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = new { title = "x", companyName = "y", description = "z", url = "https://example.com", source = "Manual", publishedAt = DateTimeOffset.UtcNow, expiresAt = (DateTimeOffset?)null };

        var response = await _client.PostAsJsonAsync("/api/v1/job-ads", command, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_then_GET_returns_created_job_ad_with_active_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var command = new
        {
            title = "Senior Backend Engineer",
            companyName = "Klarna",
            description = "Vi söker en senior backend-utvecklare.",
            url = "https://jobs.klarna.com/job/123",
            source = "Manual",
            publishedAt = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero),
            expiresAt = (DateTimeOffset?)null
        };

        var postResponse = await _client.PostAsJsonAsync("/api/v1/job-ads", command, ct);

        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var postBody = await postResponse.Content.ReadAsStringAsync(ct);
        var postDoc = JsonDocument.Parse(postBody);
        var id = postDoc.RootElement.GetProperty("id").GetString();
        id.ShouldNotBeNullOrEmpty();

        var getResponse = await _client.GetAsync($"/api/v1/job-ads/{id}", ct);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var getBody = await getResponse.Content.ReadAsStringAsync(ct);
        var getDoc = JsonDocument.Parse(getBody);
        getDoc.RootElement.GetProperty("title").GetString().ShouldBe("Senior Backend Engineer");
        getDoc.RootElement.GetProperty("status").GetString().ShouldBe("Active");
    }
}
