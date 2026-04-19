using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.MyProfile;

[Collection("Api")]
public class MeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> RegisterAndGetToken(CancellationToken ct)
    {
        var email = $"me-{Guid.NewGuid()}@example.com";
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = "T3stPwd!", displayName = "Me User" }, ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task GET_me_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_valid_token_returns_user_info()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = await RegisterAndGetToken(ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("userId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GET_me_profile_with_valid_token_returns_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = await RegisterAndGetToken(ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/me/profile", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("displayName").GetString().ShouldBe("Me User");
    }
}
