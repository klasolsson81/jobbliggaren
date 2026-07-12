using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #796 — the DEV-ONLY token-free confirmed-login seam (<c>POST /api/v1/dev/confirm-email</c>) that the
/// Playwright E2E suite drives to obtain a login-capable user against a flag-ON backend without a real
/// out-of-band email round-trip. The E2E suite exercises this end-to-end but is OBSERVE-ONLY (not a
/// required <c>ci</c> gate), so its contract would otherwise rest on no blocking check.
///
/// <para>
/// This class pins that contract in the REQUIRED lane and is the positive counterpart to
/// <c>ProductionStartupSmokeTests.POST_dev_confirm_email_is_unmapped_in_Production_env</c> (which proves
/// the 404 outside Development). The <c>ApiFactory</c> host forces Development, so both structural gates
/// are open here: the endpoint is mapped AND <c>IDevEmailConfirmer</c> resolves. It also gives the
/// Infrastructure <see cref="Jobbliggaren.Infrastructure.Auth.DevEmailConfirmer"/> (over
/// <c>UserManager</c>) its only required-gate coverage. Runs against the shared flag-ON host
/// (<c>CreateEmailConfirmationClient</c>), the same posture E2E targets.
/// </para>
/// </summary>
[Collection("Api")]
public class DevConfirmEmailEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string Password = "T3stlosen123456";

    private Task<HttpResponseMessage> RegisterAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password = Password, displayName = "Dev Confirm User" }, ct);

    private Task<HttpResponseMessage> LoginAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }, ct);

    private Task<HttpResponseMessage> ConfirmAsync(object body, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/dev/confirm-email", body, ct);

    [Fact]
    public async Task POST_dev_confirm_email_confirms_account_and_opens_the_login_gate()
    {
        // The load-bearing arc E2E depends on: a fresh account is unconfirmed (register→202) and the
        // flag-ON login gate rejects it (403 EmailNotConfirmed); the dev seam force-confirms it (204);
        // login then succeeds (200 + sessionId). Proves the endpoint wiring, the 204 contract, the real
        // DevEmailConfirmer over UserManager, AND that the confirm actually flips EmailConfirmed.
        var ct = TestContext.Current.CancellationToken;
        var email = $"dev-confirm-arc-{Guid.NewGuid()}@example.com";

        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var gateClosed = await LoginAsync(email, ct);
        gateClosed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await gateClosed.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe(AuthErrorCodes.EmailNotConfirmed);

        var confirm = await ConfirmAsync(new { email }, ct);
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var gateOpen = await LoginAsync(email, ct);
        gateOpen.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await gateOpen.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_dev_confirm_email_already_confirmed_returns_204_idempotent()
    {
        // Idempotency lives in DevEmailConfirmer (the `if (!user.EmailConfirmed)` guard) — invisible to
        // the handler unit test, which only sees the port outcome. A second confirm on an already-
        // confirmed account must still return 204 (no error), so a re-run of the E2E seed is safe.
        var ct = TestContext.Current.CancellationToken;
        var email = $"dev-confirm-idem-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await ConfirmAsync(new { email }, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConfirmAsync(new { email }, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task POST_dev_confirm_email_unknown_account_returns_404()
    {
        // No account matches → the port's NotFound outcome maps to 404 at the endpoint. Pins the
        // Confirmed→204 / NotFound→404 discrimination at the HTTP boundary (not just the handler enum).
        var ct = TestContext.Current.CancellationToken;

        var response = await ConfirmAsync(new { email = $"dev-confirm-nobody-{Guid.NewGuid()}@example.com" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_dev_confirm_email_empty_email_returns_400()
    {
        // The empty/whitespace guard lives at the ENDPOINT (Results.BadRequest before dispatch), so the
        // handler unit test cannot reach it and E2E never sends an empty body — this is the branch's
        // only coverage. Short-circuits BEFORE Mediator, so no account is touched.
        var ct = TestContext.Current.CancellationToken;

        var response = await ConfirmAsync(new { email = "" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
