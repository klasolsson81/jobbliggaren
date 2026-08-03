using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        // Asserted on the parsed ProblemDetails title, not a raw substring over the whole JSON: the
        // frontend discriminates on exactly this field (a Redis outage produces a 503 here too), so a
        // match anywhere in the body would not prove the contract the client depends on.
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("title").GetString().ShouldBe("Auth.RegistrationsClosed");
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
    /// The gate is not simply on everywhere. Strictly speaking this is subsumed — the test above
    /// already asserts 200 on the open host for the same address — but it is kept as the NAMED
    /// counterfactual: it states the property on its own rather than leaving it as a side effect of a
    /// test about something else, so deleting it would be a visible loss rather than a quiet one.
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
