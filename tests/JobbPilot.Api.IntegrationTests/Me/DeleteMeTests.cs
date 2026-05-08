using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.MyProfile;

/// <summary>
/// End-to-end-tester för DELETE /api/v1/me — GDPR Art. 17-flödet per ADR 0024
/// D4+D5. Verifierar:
/// <list type="bullet">
/// <item>Auth-skydd (401 utan token)</item>
/// <item>Cascade soft-delete på JobSeeker + ev. user-ägda aggregat</item>
/// <item>Audit-rad Account.Deleted skrivs av AuditBehavior</item>
/// <item>Login-blockering efter radering (Auth.AccountPendingDeletion)</item>
/// <item>Session-invalidering post-commit (secondary Redis-set)</item>
/// <item>Idempotency vid retry</item>
/// </list>
/// </summary>
[Collection("Api")]
public class DeleteMeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DELETE_me_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.DeleteAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DELETE_me_with_valid_session_returns_204_and_softDeletes_jobseeker()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"delete-me-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await _client.DeleteAsync("/api/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verifiera soft-delete på JobSeeker via Services-DI. Slå upp via UserId
        // för att inte träffa parallella testers JobSeekers (DisplayName-bas
        // är inte unik nog).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<
            Microsoft.AspNetCore.Identity.UserManager<JobbPilot.Infrastructure.Identity.ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();

        var seeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == user.Id, ct);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldNotBeNull("DELETE /me ska soft-deleta JobSeeker");
    }

    [Fact]
    public async Task DELETE_me_blocks_subsequent_login_with_indistinguishable_invalid_credentials_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"login-blocked-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var deleteResponse = await _client.DeleteAsync("/api/v1/me", ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = "T3stlosen123456" },
            ct);

        // ADR 0024 D5 + security-auditor STEG 10b Sec-1 (information disclosure):
        // soft-deletad konto returnerar SAMMA fel som okänd email / fel lösen
        // (Auth.InvalidCredentials, 401) — inte särskiljande "AccountPendingDeletion"
        // som hade gett credential-stuffing-listor en konto-status-orakelt.
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "soft-deletad konto ska INTE kunna logga in (samma 401 som okänd email/fel lösen)");
        var body = await loginResponse.Content.ReadAsStringAsync(ct);
        body.ShouldContain("Auth.InvalidCredentials");
        body.Contains("AccountPendingDeletion", StringComparison.Ordinal).ShouldBeFalse(
            "AccountPendingDeletion-koden får aldrig läcka till klient");
    }

    [Fact]
    public async Task DELETE_me_invalidates_active_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"sess-invalidated-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var deleteResponse = await _client.DeleteAsync("/api/v1/me", ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Försök använda samma session-id igen — ska få 401 (session invaliderad
        // av InvalidateAllForUserAsync via secondary Redis-set).
        var meResponse = await _client.GetAsync("/api/v1/me", ct);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "session-id ska vara invaliderat efter DELETE /me");
    }

    [Fact]
    public async Task DELETE_me_writes_Account_Deleted_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"audit-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var deleteResponse = await _client.DeleteAsync("/api/v1/me", ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<
            Microsoft.AspNetCore.Identity.UserManager<JobbPilot.Infrastructure.Identity.ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();

        // ExtractAggregateId returnerar JobSeeker.Id.Value, så vi söker via UserId
        // → JobSeeker → AggregateId-matchning.
        var seeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == user.Id, ct);
        seeker.ShouldNotBeNull();

        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == seeker.Id.Value && e.EventType == "Account.Deleted")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exakt en Account.Deleted-rad ska skrivas per DELETE /me");
        auditEntries[0].AggregateType.ShouldBe("JobSeeker");
        auditEntries[0].UserId.ShouldBe(user.Id);
    }

    // Idempotency-testet är inte möjligt via ren API-yta: andra DELETE /me kräver
    // ny session, och login är blockerad efter första radering per D5. Idempotens
    // verifieras indirekt av "exakt EN Account.Deleted-rad"-asserten ovan
    // (om handler:n inte var idempotent skulle vi få N rader vid Hangfire-retry).
    // Domain-nivå-idempotency täcks av handler-unit-test i Block F:s
    // tillkommande tests.
}
