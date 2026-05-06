using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Auth;

[Collection("Api")]
public class RegisterTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_register_with_valid_data_returns_session_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"reg-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "Test User",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_register_with_duplicate_email_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"dup-{Guid.NewGuid()}@example.com";
        var body = new { email, password = "T3stlosen123456", displayName = "First User" };

        await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);
        var second = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_register_with_blank_display_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"blank-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "   ",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
