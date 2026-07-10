using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #616 — end-to-end pins for the breached-password rejection on BOTH set-password paths, driven
/// through the full stack (endpoint → Mediator pipeline → UserManager → PwnedPasswordValidator →
/// UserAccountService error mapping → ProblemDetails). Verdicts are steered per password via the
/// ApiFactory stub (no network). Pins:
/// <list type="bullet">
/// <item>Breached password at register → 400 with ProblemDetails title <c>Auth.PwnedPassword</c>
/// (the machine code the frontend whitelists) and a detail free of breach source/count</item>
/// <item>Breached NEW password at change-password → same 400; the old password still works</item>
/// <item>Checker Unavailable → BOTH flows succeed (fail-open end-to-end, CTO-bind FORK 1)</item>
/// </list>
/// Unique passwords per test — the stub dictionary is shared across the "Api" collection.
/// </summary>
[Collection("Api")]
public class BreachedPasswordTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_register_with_breached_password_returns_400_with_PwnedPassword_title()
    {
        var ct = TestContext.Current.CancellationToken;
        // Hardcoded TEST fixture password, not a real secret. gitleaks:allow
        const string breached = "BreachedReg123456";
        _factory.BreachChecks.SetVerdict(breached, BreachCheckVerdict.Breached);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"pwned-reg-{Guid.NewGuid()}@example.se", password = breached, displayName = "Test User" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.PwnedPassword");
        var detail = json.GetProperty("detail").GetString().ShouldNotBeNull();
        detail.ShouldBe("Lösenordet har förekommit i kända dataläckor. Välj ett annat lösenord.");
    }

    [Fact]
    public async Task POST_register_when_breach_check_unavailable_succeeds_FailOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        // Hardcoded TEST fixture password, not a real secret.
        const string password = "UnavailableReg123456"; // gitleaks:allow
        _factory.BreachChecks.SetVerdict(password, BreachCheckVerdict.Unavailable);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"pwned-open-{Guid.NewGuid()}@example.se", password, displayName = "Test User" },
            ct);

        // CTO-bind FORK 1 end-to-end: an HIBP outage must never block registration.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("sessionId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task POST_change_password_with_breached_new_password_returns_400_with_PwnedPassword_title()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"pwned-cp-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        // Hardcoded TEST fixture password, not a real secret. gitleaks:allow
        const string breached = "BreachedChange123456";
        _factory.BreachChecks.SetVerdict(breached, BreachCheckVerdict.Breached);

        var response = await ChangePasswordAsync(
            sessionId, AuthTestHelpers.DefaultTestPassword, breached, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.PwnedPassword");

        // The password did NOT change — the original still logs in.
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = AuthTestHelpers.DefaultTestPassword }, ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_change_password_when_breach_check_unavailable_succeeds_FailOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"pwned-cpopen-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        // Hardcoded TEST fixture password, not a real secret. gitleaks:allow
        const string newPassword = "UnavailableChange123456";
        _factory.BreachChecks.SetVerdict(newPassword, BreachCheckVerdict.Unavailable);

        var response = await ChangePasswordAsync(
            sessionId, AuthTestHelpers.DefaultTestPassword, newPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The change went through: the NEW password logs in.
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = newPassword }, ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(
        string sessionId, string current, string updated, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = current, newPassword = updated }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(req, ct);
    }
}
