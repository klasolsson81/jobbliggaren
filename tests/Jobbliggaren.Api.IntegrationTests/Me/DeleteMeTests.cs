using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// server-enforced re-autentisering (PR2c/C5, epik #481; en ändamålsbunden grant sedan #1739, ADR 0142
/// D5). Kontot är det lösenordslösa D10-formatet, och varje grant mintas genom produktionen: sessionen
/// begär en kod till kontots egen adress, koden läses ur mejlet och presenteras. Verifierar:
/// <list type="bullet">
/// <item>Auth-skydd (401 utan token)</item>
/// <item>Färsk grant → cascade soft-delete + Account.Deleted-audit + session-invalidering (204)</item>
/// <item>Oanvändbar grant → 401 och kontot raderas INTE (en kapad session ensam räcker inte)</item>
/// <item>Tom/saknad grant → 400 (ValidationBehavior före re-auth) och kontot raderas inte</item>
/// <item>Grant-vägrans-paritet: en okänd grant, en förbrukad, en annan användares och dess ägares därefter
///   ger byte-identisk 401 — ingen av dem avslöjar varför</item>
/// </list>
///
/// OBS rate-limit: AccountDeletion-policyn är UserId-partitionerad med PermitLimit=1/60s och hålls
/// på default i testmiljön, så varje user träffar POST /me/delete HÖGST en gång per test. Re-auth-
/// cooldownen är 1 kod per user och 60 s, så varje grant mintas av en egen user.
/// </summary>
[Collection("Api")]
public class DeleteMeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // A well-formed grant nobody minted: a test fixture, not a secret.
    private const string UnusableGrant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"delete-{label}-{Guid.NewGuid():N}@example.se";

    // POST /me/delete med Bearer-session + grant i bodyn. Sätter Authorization per anrop så varje
    // anrop är självständigt (t.ex. paritetstestet som växlar mellan konton).
    private async Task<HttpResponseMessage> PostDeleteAsync(string sessionId, string? reauthGrant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/delete")
        {
            Content = JsonContent.Create(new { reauthGrant }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(request, Ct);
    }

    private Task<string> MintGrantAsync(string sessionId, string email) =>
        ReauthTestHelpers.MintGrantAsync(_factory, _client, sessionId, email, Ct);

    // Slår upp seekern via UserId (email → ApplicationUser → JobSeeker) i en egen server-scope, obeoende
    // av HTTP-sessionens tillstånd. IgnoreQueryFilters så soft-deletade rader syns.
    private async Task<JobSeeker?> LoadSeekerByEmailAsync(string email)
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
            .FirstOrDefaultAsync(js => js.UserId == user.Id, Ct);
    }

    // The 401 body with the per-request trace id removed: what a caller could compare across attempts.
    private static async Task<string> ComparableBodyAsync(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }

    [Fact]
    public async Task POST_me_delete_without_token_returns_401()
    {
        // Ingen Authorization-header → RequireAuthorization returnerar 401 före endpointen (och före
        // re-auth), oavsett body.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/delete", new { reauthGrant = UnusableGrant }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_me_delete_with_a_fresh_grant_returns_204_and_softDeletes_jobseeker()
    {
        var email = NewAddress("ok");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var grant = await MintGrantAsync(sessionId, email);

        var response = await PostDeleteAsync(sessionId, grant);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var seeker = await LoadSeekerByEmailAsync(email);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldNotBeNull("POST /me/delete med en färsk grant ska soft-deleta JobSeeker");
    }

    [Fact]
    public async Task POST_me_delete_with_an_unusable_grant_returns_401_and_does_not_delete()
    {
        // Kärn-assertionen för PR2c: en (kapad) giltig session ENSAM räcker inte — utan en grant som
        // kontots egen inkorg gett gatar ReauthenticationBehavior operationen och handlern körs aldrig.
        var email = NewAddress("unusable");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

        var response = await PostDeleteAsync(sessionId, UnusableGrant);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Kontot lever fortfarande — ingen soft-delete skedd.
        var seeker = await LoadSeekerByEmailAsync(email);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldBeNull("en oanvändbar grant får INTE radera kontot");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_me_delete_with_missing_or_empty_grant_returns_400_and_does_not_delete(string? reauthGrant)
    {
        // ValidationBehavior (NotEmpty) kör FÖRE ReauthenticationBehavior, så tom/saknad grant är
        // 400 (validering) — inte 401 (re-auth). Tom vs oanvändbar = 400 vs 401 avslöjar inget om kontot.
        var email = NewAddress("empty");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

        var response = await PostDeleteAsync(sessionId, reauthGrant);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Handlern körs aldrig (validering kortsluter) → kontot lever.
        var seeker = await LoadSeekerByEmailAsync(email);
        seeker.ShouldNotBeNull();
        seeker.DeletedAt.ShouldBeNull("400-validering ska inte radera kontot");
    }

    [Fact]
    public async Task POST_me_delete_answers_every_refused_grant_with_one_byte_identical_401()
    {
        // ADR 0142 D3 + GDPR Art. 32 oracle-avoidance på delete-vägen: en okänd grant, en förbrukad, en annan
        // användares (fel bindning) och dess ägares därefter (bränd av den främmande inlösningen) renderar EN
        // 401 — annars vore inlösningsstatus ett orakel om vems grant som visats var. Varje premiss produceras
        // av produktionen: den förbrukade spenderades av /auth/change-password (behavioren löser in före
        // handlern, som sedan vägrar ett lösenordslöst konto), den främmande mintades av sin egen session.
        // Varje konto träffar /me/delete EN gång (AccountDeletion-limit=1/user).

        // Konto A — en grant ingen mintade.
        var emailA = NewAddress("parity-unknown");
        var sessionA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, emailA, ct: Ct);
        var unknown = await PostDeleteAsync(sessionA, UnusableGrant);

        // Konto B — en grant som redan är förbrukad.
        var emailB = NewAddress("parity-spent");
        var sessionB = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, emailB, ct: Ct);
        var grantB = await MintGrantAsync(sessionB, emailB);
        using (var spend = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new { reauthGrant = grantB, currentPassword = "x", newPassword = new string('a', 12) }),
        })
        {
            spend.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionB);
            var spent = await _client.SendAsync(spend, Ct);
            spent.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, "the first redemption of a fresh grant succeeds");
        }
        var replayed = await PostDeleteAsync(sessionB, grantB);

        // Konto C visar konto D:s grant; konto D visar sedan sin egen, som C:s försök brände.
        var emailC = NewAddress("parity-stranger");
        var sessionC = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, emailC, ct: Ct);
        var emailD = NewAddress("parity-owner");
        var sessionD = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, emailD, ct: Ct);
        var grantD = await MintGrantAsync(sessionD, emailD);
        var stranger = await PostDeleteAsync(sessionC, grantD);
        var burnedOwner = await PostDeleteAsync(sessionD, grantD);

        foreach (var response in new[] { unknown, replayed, stranger, burnedOwner })
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await ComparableBodyAsync(unknown);
        (await ComparableBodyAsync(replayed)).ShouldBe(body);
        (await ComparableBodyAsync(stranger)).ShouldBe(body);
        (await ComparableBodyAsync(burnedOwner)).ShouldBe(body);

        // Hard pin: den centrala 401:an renderar Auth.InvalidCredentials, aldrig ett grant-specifikt skäl.
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
        json.GetProperty("detail").GetString().ShouldBe("E-post eller lösenord är felaktigt.");

        // Inget konto raderades, och D:s konto lever trots att en riktig grant fanns för det.
        foreach (var email in new[] { emailA, emailB, emailC, emailD })
            (await LoadSeekerByEmailAsync(email)).ShouldNotBeNull().DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task POST_me_delete_invalidates_active_sessions()
    {
        var email = NewAddress("sessions");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var grant = await MintGrantAsync(sessionId, email);

        var deleteResponse = await PostDeleteAsync(sessionId, grant);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Försök använda samma session-id igen — ska få 401 (session invaliderad av
        // InvalidateAllForUserAsync + :deleted-tombstone).
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        var meResponse = await _client.SendAsync(me, Ct);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "session-id ska vara invaliderat efter POST /me/delete");
    }

    [Fact]
    public async Task POST_me_delete_writes_Account_Deleted_audit_entry()
    {
        var email = NewAddress("audit");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var grant = await MintGrantAsync(sessionId, email);

        var deleteResponse = await PostDeleteAsync(sessionId, grant);
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
            .FirstOrDefaultAsync(js => js.UserId == user.Id, Ct);
        seeker.ShouldNotBeNull();

        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == seeker.Id.Value && e.EventType == "Account.Deleted")
            .ToListAsync(Ct);

        auditEntries.Count.ShouldBe(1, "exakt en Account.Deleted-rad ska skrivas per POST /me/delete");
        auditEntries[0].AggregateType.ShouldBe("JobSeeker");
        auditEntries[0].UserId.ShouldBe(user.Id);
    }

    // Idempotency-testet är inte möjligt via ren API-yta: en andra POST /me/delete kräver ny session,
    // och login är blockerad efter första radering per D5 (dessutom kapar AccountDeletion-rate-limiten
    // en andra delete inom samma minut). Idempotens verifieras indirekt av "exakt EN Account.Deleted-
    // rad"-asserten ovan (om handlern inte var idempotent skulle vi få N rader vid Hangfire-retry) och
    // direkt av handler-unit-testet i DeleteAccountCommandHandlerTests. Att ett raderat konto inte kan
    // logga in pinnas där inloggningen bor: LoginChallengeProofTests
    // (An_account_deleted_after_the_mail_went_out_gets_its_deletion_date_not_a_session).
}
