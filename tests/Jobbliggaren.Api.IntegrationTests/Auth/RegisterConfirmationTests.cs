using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #714 — email-confirmation-first registration (POST /api/v1/auth/register with
/// Auth:RequireEmailConfirmation ON). The whole point is to close the 200-vs-400 account-enumeration
/// status oracle, so the load-bearing assertions are the PARITY tests: a fresh and a taken address are
/// indistinguishable on both status AND body. Runs against a flag-ON host over the ApiFactory's shared
/// Testcontainers + recording IEmailSender.
/// </summary>
[Collection("Api")]
public class RegisterConfirmationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string StrongPassword = "T3stlosen123456";

    private Task<HttpResponseMessage> RegisterAsync(string email, string password, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password, displayName = "Test User" },
            ct);

    [Fact]
    public async Task POST_register_fresh_returns_202_no_session_and_queues_confirmation_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"regconf-fresh-{Guid.NewGuid()}@example.com";

        var response = await RegisterAsync(email, StrongPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty("202 carries no session-id body");

        // The out-of-band confirmation link is queued to the fresh address (the only signal).
        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.EmailConfirmation);
        // A fresh signup does NOT get an account-exists notice.
        _factory.Emails.Sent.ShouldNotContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.AccountExistsNotice);
    }

    [Fact]
    public async Task POST_register_taken_returns_202_and_queues_account_exists_notice()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"regconf-taken-{Guid.NewGuid()}@example.com";

        // First registration creates the account (and queues a confirmation link).
        (await RegisterAsync(email, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Second registration for the SAME address is a duplicate — swallowed to the same 202, with an
        // out-of-band account-exists notice instead of a confirmation link.
        var second = await RegisterAsync(email, StrongPassword, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await second.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty();

        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.AccountExistsNotice);
    }

    [Fact]
    public async Task POST_register_fresh_and_taken_are_indistinguishable_on_status_and_body()
    {
        // THE anti-enumeration invariant (CTO-bind Risk 1): for a fixed strong password, a taken and a
        // fresh address must produce byte-identical responses (status + body). If they diverge, the
        // status oracle is re-opened.
        var ct = TestContext.Current.CancellationToken;
        var takenEmail = $"regconf-parity-taken-{Guid.NewGuid()}@example.com";
        var freshEmail = $"regconf-parity-fresh-{Guid.NewGuid()}@example.com";

        // Make takenEmail exist first.
        (await RegisterAsync(takenEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var takenResponse = await RegisterAsync(takenEmail, StrongPassword, ct);
        var freshResponse = await RegisterAsync(freshEmail, StrongPassword, ct);

        takenResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        freshResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        takenResponse.StatusCode.ShouldBe(freshResponse.StatusCode);

        var takenBody = await takenResponse.Content.ReadAsStringAsync(ct);
        var freshBody = await freshResponse.Content.ReadAsStringAsync(ct);
        takenBody.ShouldBe(freshBody, "a taken and a fresh address must be indistinguishable on the body");
        takenBody.ShouldBeNullOrEmpty("neither response carries a session-id (no instant login)");
    }

    [Fact]
    public async Task POST_register_breached_password_returns_identical_400_for_fresh_and_taken()
    {
        // CTO-bind Risk 1 (breached-vs-duplicate ordering): the only register 400 that remains under the
        // flag is a breached password, and it is credential-dependent, NOT existence-dependent —
        // Identity validates the password BEFORE uniqueness, so a taken and a fresh address BOTH get the
        // same Auth.PwnedPassword 400 for a breached password. This pins that no breached-vs-duplicate
        // status oracle exists.
        var ct = TestContext.Current.CancellationToken;
        var breachedPassword = $"Breached-{Guid.NewGuid():N}";
        _factory.BreachChecks.SetVerdict(breachedPassword, BreachCheckVerdict.Breached);

        // Make an address exist (with a strong password), then re-register it with the breached one.
        var takenEmail = $"regconf-breach-taken-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(takenEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var freshEmail = $"regconf-breach-fresh-{Guid.NewGuid()}@example.com";

        var takenBreached = await RegisterAsync(takenEmail, breachedPassword, ct);
        var freshBreached = await RegisterAsync(freshEmail, breachedPassword, ct);

        takenBreached.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        freshBreached.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var takenTitle = (await takenBreached.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("title").GetString();
        var freshTitle = (await freshBreached.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("title").GetString();

        takenTitle.ShouldBe("Auth.PwnedPassword");
        freshTitle.ShouldBe(takenTitle, "a breached password is identical for a taken and a fresh address");
    }

    [Fact]
    public async Task POST_register_send_failure_is_symmetric_for_fresh_and_taken()
    {
        // CTO-bind Risk 1 (symmetry guard): a transport failure on the email send must yield the SAME
        // response for a fresh address (confirmation send) and a taken address (account-exists-notice
        // send), or the failure MODE itself becomes an enumeration distinguisher. Both branches send as
        // their final action and let the exception propagate, so both are the identical server error.
        var ct = TestContext.Current.CancellationToken;
        var takenEmail = $"regconf-sendfail-taken-{Guid.NewGuid()}@example.com";

        // Create the taken address first via the normal (working-sender) flag-ON client.
        (await RegisterAsync(takenEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var throwingClient = CreateThrowingSenderClient();
        var freshEmail = $"regconf-sendfail-fresh-{Guid.NewGuid()}@example.com";

        var takenResult = await throwingClient.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = takenEmail, password = StrongPassword, displayName = "Test User" }, ct);
        var freshResult = await throwingClient.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = freshEmail, password = StrongPassword, displayName = "Test User" }, ct);

        // Both branches fail identically (the account-exists-notice send and the confirmation send both
        // throw). Assert a server error AND symmetry, without hard-coding the exact 5xx status.
        ((int)takenResult.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
        takenResult.StatusCode.ShouldBe(
            freshResult.StatusCode, "a send failure must be symmetric across the fresh and taken branches");
    }

    // Flag-ON host whose IEmailSender throws on the two registration sends — for the symmetry guard.
    private HttpClient CreateThrowingSenderClient() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<AuthOptions>(o => o.RequireEmailConfirmation = true);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new ThrowingEmailSender());
        })).CreateClient();
}
