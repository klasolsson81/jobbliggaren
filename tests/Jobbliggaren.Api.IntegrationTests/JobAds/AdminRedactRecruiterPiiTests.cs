using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// POST /api/v1/admin/job-ads/redact-recruiter-pii — <b>disabled, returns 501</b> (#842).
///
/// <para>
/// <b>What this file used to be, and why that matters.</b> It used to assert that the
/// endpoint erased recruiter PII. It did so by hand-seeding <c>raw_payload</c> with an
/// <c>employer.contact_email</c> key through <c>JobAd.Import</c> directly, bypassing
/// <c>PlatsbankenJobSource</c> and <c>JobTechPayloadSanitizer</c> — the sanitizer whose
/// default-deny allowlist <i>guarantees</i> that key is stripped in production. It also
/// set <c>description: "d"</c>, so the free-text case never ran.
/// </para>
///
/// <para>
/// The test therefore constructed a state production can never reach, and asserted
/// against it. It was green for two releases while the only Art. 17 erasure path in the
/// system erased nothing: measured against the real corpus, <b>0 of 93 469</b> ingested
/// ads carry the probed key, so <c>rowsAffected = 0</c> was its only possible outcome —
/// and the endpoint returned 200 OK anyway. <b>The green test is what hid the bug.</b>
/// </para>
///
/// <para>
/// Standing rule this file now obeys (#843, bound by senior-cto-advisor 2026-07-13):
/// <i>tests for ingest-derived state MUST construct that state through the production
/// write path (real ACL, real sanitizer, real Import/UpdateFromSource). Hand-seeding a
/// column that production writes only through a funnel proves nothing about production.</i>
/// The Tier-A ingest tests (ADR 0106, PR2) are written that way, against real Postgres.
/// </para>
///
/// <para>
/// What survives here is the part of the feature that was never broken: the admin
/// authorization gate. Plus a pin on the 501, so nothing silently re-enables a route
/// that would report a false erasure to a data subject (Art. 12(3)).
/// </para>
/// </summary>
[Collection("Api")]
public class AdminRedactRecruiterPiiTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly object AnyRequest = new { identifier = "alice@example.com", type = "Email" };

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii", AnyRequest, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_admin_returns_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii", AnyRequest, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The containment pin. An admin — the one caller who WOULD be answering a real Art. 17
    /// request — must be told the truth, not served a 200 that means nothing was erased.
    /// If someone re-enables this route without a working erasure path, this test fails.
    /// </summary>
    [Fact]
    public async Task Admin_request_returns_501_because_no_erasure_path_exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii", AnyRequest, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("title").GetString().ShouldBe("Ingen raderingsväg finns ännu");

        // The operator must be pointed at the escalation path, not left to improvise.
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("#842");
    }

    /// <summary>
    /// The response must NOT carry the old success shape. A caller (or a runbook, or a
    /// script) that reads <c>rowsAffected</c> must break loudly rather than read 0 and
    /// conclude "nothing matched, so there was nothing to erase" — which is precisely the
    /// false inference the old endpoint invited on every single call.
    /// </summary>
    [Fact]
    public async Task Response_does_not_carry_a_rowsAffected_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii", AnyRequest, ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        body.TryGetProperty("rowsAffected", out _).ShouldBeFalse(
            "A rowsAffected field would let a caller infer an erasure outcome from a route "
            + "that performs no erasure.");
    }

    private async Task<HttpClient> CreateAdminClientAsync(HttpClient client, CancellationToken ct)
    {
        var email = $"admin-redact-{Guid.NewGuid():N}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userId = Guid.Parse(meJson.GetProperty("userId").GetString()!);

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User saknas.");
        await userManager.AddToRoleAsync(user, Roles.Admin);

        var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var newSession = await sessionStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSession.Id.Reveal());

        return client;
    }
}
