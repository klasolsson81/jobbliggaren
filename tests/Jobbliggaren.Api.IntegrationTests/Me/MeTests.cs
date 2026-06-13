using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

[Collection("Api")]
public class MeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GET_me_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_valid_session_returns_user_info()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await _client.GetAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("userId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GET_me_profile_with_valid_session_returns_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, displayName: "Me User", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await _client.GetAsync("/api/v1/me/profile", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("displayName").GetString().ShouldBe("Me User");
    }
}
