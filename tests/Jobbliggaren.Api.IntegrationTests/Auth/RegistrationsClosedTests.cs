using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// ADR 0083 Amendment 2026-08-03 — the public-registration kill-switch, end to end.
/// <para>
/// The load-bearing test here is the COUNTERFACTUAL: a host with the switch CLOSED must refuse, and a
/// host with it OPEN must still succeed. Either assertion alone is compatible with a gate that does
/// nothing (or with one stuck on), which is exactly the failure mode a green suite hides.
/// </para>
/// </summary>
[Collection("Api")]
public class RegistrationsClosedTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _closed = factory.CreateRegistrationsClosedClient();

    private const string StrongPassword = "T3stlosen123456";

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, CancellationToken ct)
        => client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword, displayName = "Test User" },
            ct);

    [Fact]
    public async Task POST_register_returns_503_when_the_switch_is_closed()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await RegisterAsync(_closed, $"regclosed-{Guid.NewGuid()}@example.com", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("Auth.RegistrationsClosed");
    }

    /// <summary>
    /// The refusal must leave NOTHING behind. Proven without reaching into the database: register the
    /// same address again against the OPEN host — it succeeds with a session. Had the closed attempt
    /// created an Identity user, this second call would come back a duplicate instead.
    /// </summary>
    [Fact]
    public async Task A_refused_registration_creates_no_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"regclosed-leaves-nothing-{Guid.NewGuid()}@example.com";

        (await RegisterAsync(_closed, email, ct)).StatusCode
            .ShouldBe(HttpStatusCode.ServiceUnavailable);

        var onOpenHost = await RegisterAsync(_factory.CreateClient(), email, ct);

        onOpenHost.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the address must still be free — a duplicate would prove the refused attempt wrote a user");
    }

    /// <summary>
    /// The other half of the counterfactual: the default host is OPEN, so the gate is not simply on
    /// everywhere. Without this, the two tests above would also pass against a permanently closed app.
    /// </summary>
    [Fact]
    public async Task POST_register_still_succeeds_on_the_open_host()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await RegisterAsync(
            _factory.CreateClient(), $"regopen-{Guid.NewGuid()}@example.com", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
