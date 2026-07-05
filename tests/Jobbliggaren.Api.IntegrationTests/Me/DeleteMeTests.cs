using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

/// <summary>
/// End-to-end-tester för POST /api/v1/me/delete — GDPR Art. 17-flödet per ADR 0024 D4+D5 MED
/// server-enforced re-autentisering (PR2c/C5, epik #481). Endpointen är nu POST /me/delete (inte
/// DELETE /me) och BÄR ett lösenord i bodyn (<c>DeleteAccountRequest</c>); <c>ReauthenticationBehavior</c>
/// verifierar det FÖRE handlern körs. Verifierar:
/// <list type="bullet">
/// <item>Auth-skydd (401 utan token)</item>
/// <item>Rätt lösenord → cascade soft-delete + Account.Deleted-audit + session-invalidering (204)</item>
/// <item>FEL lösenord → 401 och kontot raderas INTE (en kapad session ensam räcker inte)</item>
/// <item>Tomt/saknat lösenord → 400 (ValidationBehavior före re-auth) och kontot raderas inte</item>
/// <item>Oracle-paritet: fel-lösenord-401 är byte-identisk med låst-konto-401 (ingen orakel)</item>
/// <item>Login-blockering efter radering (samma 401 som okänd email/fel lösen)</item>
/// </list>
///
/// OBS rate-limit: AccountDeletion-policyn är UserId-partitionerad med PermitLimit=1/60s och hålls
/// på default i testmiljön, så varje user träffar POST /me/delete HÖGST en gång per test. Låsnings-
/// scenariot lockar därför via /auth/verify (AuthWrite höjd i ApiFactory) och gör bara EN delete.
/// </summary>
[Collection("Api")]
public class DeleteMeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // POST /me/delete med Bearer-session + lösenord i bodyn. Sätter Authorization per anrop så varje
    // anrop är självständigt (t.ex. oracle-testet som växlar mellan två konton).
    private Task<HttpResponseMessage> PostDeleteAsync(string sessionId, string? password, CancellationToken ct)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return _client.PostAsJsonAsync("/api/v1/me/delete", new { password }, ct);
    }

    // Slår upp seekern via UserId (email → ApplicationUser → JobSeeker) i en egen server-scope, obeoende
    // av HTTP-sessionens tillstånd. IgnoreQueryFilters så soft-deletade rader syns.
    private async Task<JobSeeker?> LoadSeekerByEmailAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return null;

        return await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == user.Id, ct);
    }

    [Fact]
    public async Task POST_me_delete_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Ingen Authorization-header → RequireAuthorization returnerar 401 före endpointen (och före
        // re-auth), oavsett body.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/delete", new { password = AuthTestHelpers.DefaultTestPassword }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_me_delete_with_correct_password_returns_204_and_softDeletes_jobseeker()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"delete-me-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);

        var response = await PostDeleteAsync(sessionId, AuthTestHelpers.DefaultTestPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var seeker = await LoadSeekerByEmailAsync(email, ct);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldNotBeNull("POST /me/delete med rätt lösenord ska soft-deleta JobSeeker");
    }

    [Fact]
    public async Task POST_me_delete_with_wrong_password_returns_401_and_does_not_delete()
    {
        // Kärn-assertionen för PR2c: en (kapad) giltig session ENSAM räcker inte — utan rätt lösenord
        // gatar ReauthenticationBehavior operationen och handlern körs aldrig.
        var ct = TestContext.Current.CancellationToken;
        var email = $"delete-wrong-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);

        var response = await PostDeleteAsync(sessionId, "FelLosen!", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Kontot lever fortfarande — ingen soft-delete skedd.
        var seeker = await LoadSeekerByEmailAsync(email, ct);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldBeNull("fel lösenord får INTE radera kontot");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_me_delete_with_missing_or_empty_password_returns_400_and_does_not_delete(string? password)
    {
        // ValidationBehavior (NotEmpty) kör FÖRE ReauthenticationBehavior, så tomt/saknat lösenord är
        // 400 (validering) — inte 401 (re-auth). Tomt vs fel = 400 vs 401 avslöjar inget om kontot.
        var ct = TestContext.Current.CancellationToken;
        var email = $"delete-empty-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);

        var response = await PostDeleteAsync(sessionId, password, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Handlern körs aldrig (validering kortsluter) → kontot lever.
        var seeker = await LoadSeekerByEmailAsync(email, ct);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldBeNull("400-validering ska inte radera kontot");
    }

    [Fact]
    public async Task POST_me_delete_wrong_password_401_is_byte_identical_to_locked_account_no_oracle()
    {
        // ADR 0024 D5 + GDPR Art. 32 oracle-avoidance, utsträckt till delete-vägen: ett FEL lösenord
        // och ett LÅST konto (rätt lösenord) måste rendera byte-identisk 401 — annars blir lås-status
        // ett enumererings-/DoS-orakel. Central 401 kommer från ReauthenticationFailedException →
        // AuthProblem (samma källa som /auth/verify), som alltid renderar Auth.InvalidCredentials och
        // aldrig det interna Auth.AccountLocked.
        var ct = TestContext.Current.CancellationToken;
        var password = AuthTestHelpers.DefaultTestPassword;

        // Konto A — vanligt fel lösenord (ej låst). EN delete (AccountDeletion-limit=1/user).
        var emailA = $"delete-oracle-wrong-{Guid.NewGuid()}@example.se";
        var sessionA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, emailA, password, ct: ct);
        var wrongResponse = await PostDeleteAsync(sessionA, "FelLosen!", ct);

        // Konto B — lås via 5 misslyckade /auth/verify (AuthWrite höjd i test → ingen 429), sedan EN
        // /me/delete med RÄTT lösenord. Låst → ValidateCredentials avvisar → samma centrala 401.
        // /verify används för lås-loopen så /me/delete träffas bara EN gång (limit=1/user).
        var emailB = $"delete-oracle-locked-{Guid.NewGuid()}@example.se";
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, emailB, password, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);
        for (var i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("/api/v1/auth/verify", new { password = "FelLosen!" }, ct);
        var lockedResponse = await PostDeleteAsync(sessionB, password, ct);

        wrongResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        lockedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var wrongJson = await wrongResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var lockedJson = await lockedResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Identisk title OCH detail → inget orakel mellan "fel lösen" och "låst" på delete-vägen.
        wrongJson.GetProperty("title").GetString()
            .ShouldBe(lockedJson.GetProperty("title").GetString());
        wrongJson.GetProperty("detail").GetString()
            .ShouldBe(lockedJson.GetProperty("detail").GetString());
        // Hard pin: den centrala 401:an renderar Auth.InvalidCredentials (aldrig interna Auth.AccountLocked).
        wrongJson.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
        wrongJson.GetProperty("detail").GetString().ShouldBe("E-post eller lösenord är felaktigt.");
    }

    [Fact]
    public async Task POST_me_delete_blocks_subsequent_login_with_indistinguishable_invalid_credentials_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"login-blocked-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);

        var deleteResponse = await PostDeleteAsync(sessionId, AuthTestHelpers.DefaultTestPassword, ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = AuthTestHelpers.DefaultTestPassword },
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
    public async Task POST_me_delete_invalidates_active_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"sess-invalidated-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);

        var deleteResponse = await PostDeleteAsync(sessionId, AuthTestHelpers.DefaultTestPassword, ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Försök använda samma session-id igen — ska få 401 (session invaliderad av
        // InvalidateAllForUserAsync + :deleted-tombstone).
        var meResponse = await _client.GetAsync("/api/v1/me", ct);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "session-id ska vara invaliderat efter POST /me/delete");
    }

    [Fact]
    public async Task POST_me_delete_writes_Account_Deleted_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"audit-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email: email, ct: ct);

        var deleteResponse = await PostDeleteAsync(sessionId, AuthTestHelpers.DefaultTestPassword, ct);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();

        // ExtractAggregateId returnerar JobSeeker.Id.Value, så vi söker via UserId → JobSeeker →
        // AggregateId-matchning.
        var seeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == user.Id, ct);
        seeker.ShouldNotBeNull();

        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == seeker.Id.Value && e.EventType == "Account.Deleted")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exakt en Account.Deleted-rad ska skrivas per POST /me/delete");
        auditEntries[0].AggregateType.ShouldBe("JobSeeker");
        auditEntries[0].UserId.ShouldBe(user.Id);
    }

    // Idempotency-testet är inte möjligt via ren API-yta: en andra POST /me/delete kräver ny session,
    // och login är blockerad efter första radering per D5 (dessutom kapar AccountDeletion-rate-limiten
    // en andra delete inom samma minut). Idempotens verifieras indirekt av "exakt EN Account.Deleted-
    // rad"-asserten ovan (om handlern inte var idempotent skulle vi få N rader vid Hangfire-retry) och
    // direkt av handler-unit-testet i DeleteAccountCommandHandlerTests.
}
