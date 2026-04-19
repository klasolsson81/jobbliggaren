using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Auth;

[Collection("Api")]
public class LoginTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_login_with_valid_credentials_returns_access_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"login-{Guid.NewGuid()}@example.com";
        var password = "T3stPwd!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password, displayName = "Login User" }, ct);

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_login_with_wrong_password_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"wrong-{Guid.NewGuid()}@example.com";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = "T3stPwd!", displayName = "User" }, ct);

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = "WrongPwd!" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
