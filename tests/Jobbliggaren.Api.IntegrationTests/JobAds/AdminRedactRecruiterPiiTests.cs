using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// POST /api/v1/admin/job-ads/redact-recruiter-pii — the Art. 17 erasure route (#842, ADR 0106
/// Tier B). PR1's 501 containment is lifted here: there is now a path that actually erases.
///
/// <para>
/// <b>What this file used to be, and why that matters.</b> It once asserted the endpoint erased
/// recruiter PII. It hand-seeded <c>raw_payload</c> with an <c>employer.contact_email</c> key
/// straight through <c>JobAd.Import</c> — a key the ingest sanitizer's default-deny allowlist
/// <i>guarantees</i> is stripped, and which the wire POCO cannot even emit — and set
/// <c>description: "d"</c>, so the free-text case never ran. It was green for two releases while
/// the only Art. 17 path erased nothing on every call. <b>The green test is what hid the bug.</b>
/// </para>
///
/// <para>
/// The erasure SEMANTICS are therefore proven where they can be proven honestly:
/// <see cref="RecruiterErasureIngestTests"/>, end to end through the real ACL, the real sanitizer,
/// the real funnel and real Postgres (#843 — state that production writes through a funnel is
/// constructed through that funnel, or not at all). This file covers what only the HTTP surface can:
/// the authorization gate (the one part of this feature that was never broken) and the
/// request/response CONTRACT — which is where the defect actually lived, because
/// <c>200 OK, rowsAffected: 0</c> is what let a runbook tell a named person her data was erased.
/// </para>
/// </summary>
[Collection("Api")]
public class AdminRedactRecruiterPiiTests(ApiFactory factory)
{
    private const string Route = "/api/v1/admin/job-ads/redact-recruiter-pii";

    private readonly ApiFactory _factory = factory;

    private static readonly object DryRunRequest = new
    {
        identifier = "alice.andersson@example.com",
        dryRun = true,
        confirmedJobAdIds = (Guid[]?)null,
    };

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(Route, DryRunRequest, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_admin_returns_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.PostAsJsonAsync(Route, DryRunRequest, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The route works, and it answers with an OUTCOME rather than a number. Against a corpus that
    /// holds nothing for this identifier the honest answer is <c>NoMatchingDataHeld</c> — the one
    /// sentence the old mechanism said on every request and could never actually mean.
    /// </summary>
    [Fact]
    public async Task Admin_dry_run_returns_an_explicit_outcome_not_a_bare_count()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(Route, DryRunRequest, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("outcome").GetString().ShouldBe("NoMatchingDataHeld");
        body.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
    }

    /// <summary>
    /// The old success shape must never come back. A caller — or a runbook, or a script — that reads
    /// <c>rowsAffected</c> and finds 0 concludes "nothing matched, so there was nothing to erase".
    /// That inference was false on every call the old endpoint ever served, and it is the inference
    /// the runbook told an operator to relay to a data subject as a completed erasure.
    /// </summary>
    [Fact]
    public async Task Response_carries_no_rowsAffected_field_and_reports_per_surface_counts()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(Route, DryRunRequest, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        body.TryGetProperty("rowsAffected", out _).ShouldBeFalse(
            "a bare rowsAffected is what let a 0 be read as a completed erasure.");

        // Matched vs erased, PER SURFACE. The gap between them IS the disclosure — a saved search
        // that mentions the recruiter is matched but never erased — and it is structural, so nobody
        // has to remember to mention it.
        foreach (var side in new[] { "matched", "erased" })
        {
            var counts = body.GetProperty(side);
            counts.TryGetProperty("jobAds", out _).ShouldBeTrue();
            counts.TryGetProperty("recentJobSearches", out _).ShouldBeTrue();
            counts.TryGetProperty("savedSearches", out _).ShouldBeTrue();
        }
    }

    /// <summary>
    /// <b>The mandatory dry run, enforced by the API rather than by a sentence in a runbook.</b> A
    /// destructive call must state the ad count the preceding dry run reported. Omit it, and the
    /// request is rejected: you cannot erase without having looked.
    /// </summary>
    [Fact]
    public async Task Destructive_call_without_a_confirmed_count_is_REJECTED()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(
            Route,
            new
            {
                identifier = "alice.andersson@example.com",
                dryRun = false,
                confirmedJobAdIds = (Guid[]?)null,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "erasure is irreversible and corpus-visible. The dry run is not advice.");
    }

    /// <summary>
    /// A one-character identifier would substring-match essentially every ad in the corpus. The dry
    /// run would reveal it — but a floor that makes the mistake unrepresentable beats a review step
    /// that merely makes it visible, and this is the one command where that trade is obvious.
    /// </summary>
    [Fact]
    public async Task A_dangerously_short_identifier_is_REJECTED()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(
            Route,
            new { identifier = "a", dryRun = true, confirmedJobAdIds = (Guid[]?)null },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// <b>Art. 12(3): a REJECTED rights request must leave a trace.</b> We owe the data subject the
    /// reasons we did not act and her right to complain — and we cannot produce either from a row we
    /// never wrote. <c>AuditBehavior</c> skipped every <c>Result.Failure</c> until this command
    /// opted in, so a refused request vanished silently.
    /// </summary>
    [Fact]
    public async Task A_REJECTED_request_still_writes_an_audit_row_and_never_stores_the_identifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        // Confirm an ad id that is NOT in the current match set — a stale dry-run view. The handler
        // refuses with a Conflict, and THAT rejection must still leave a trace.
        var identifier = $"rejected-{Guid.NewGuid():N}@example.com";
        var response = await adminClient.PostAsJsonAsync(
            Route,
            new { identifier, dryRun = false, confirmedJobAdIds = new[] { Guid.NewGuid() } },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payloads = await db.AuditLogEntries
            .Where(a => a.AggregateType == "RecruiterErasureRequest" && a.Payload != null)
            .Select(a => a.Payload!)
            .ToListAsync(ct);

        payloads.ShouldNotBeEmpty(
            "a refused rights request that leaves no trace is its own Art. 12(3) exposure — and "
            + "audit_log.payload has existed since ADR 0022 without a single command ever writing "
            + "it, which is why the runbook's verification query always returned NULL.");

        var mine = payloads.Where(p => p.Contains("ConfirmationMismatch", StringComparison.Ordinal))
            .ToList();
        mine.ShouldNotBeEmpty("the rejection reason belongs in the record.");

        // The identifier is HMAC'd, never stored. Writing her address into the audit row for her
        // own erasure request would make that request the last place it survives — the single most
        // absurd outcome available to us here.
        foreach (var payload in payloads)
        {
            payload.ShouldNotContain(identifier, Case.Insensitive,
                "the audit row must never carry the identifier we were asked to erase.");
            payload.ShouldContain("identifierHmac");
        }
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
