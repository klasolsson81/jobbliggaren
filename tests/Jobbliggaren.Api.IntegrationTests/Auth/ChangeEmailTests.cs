using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// End-to-end tests for POST /api/v1/auth/change-email (#679; two codes since #1739, ADR 0142 D5) — the REQUEST
/// step, re-auth-gated like /change-password. The grant is minted through production by
/// <see cref="ReauthTestHelpers"/>; on success a code goes to the NEW address and the answer is 202 with the
/// challenge id, WITHOUT changing the address and WITHOUT touching any session. Accounts are passwordless
/// (ADR 0142 D9). Runs against the ApiFactory's recording IEmailSender and real Testcontainers Postgres/Redis.
/// </summary>
[Collection("Api")]
public class ChangeEmailTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // A well-formed grant nobody minted: a test fixture, not a secret.
    private const string UnusableGrant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow

    private static string Address(string label) => $"ce-{label}-{Guid.NewGuid():N}@example.se";

    private Task<HttpResponseMessage> ChangeAsync(string sessionId, string? reauthGrant, string? newEmail, CancellationToken ct) =>
        ReauthTestHelpers.PostAsSessionAsync(_client, sessionId, "/api/v1/auth/change-email", new { reauthGrant, newEmail }, ct);

    private Task<string> MintGrantAsync(string sessionId, string email, CancellationToken ct) =>
        ReauthTestHelpers.MintGrantAsync(_factory, _client, sessionId, email, ct);

    private List<RecordedLoginChallenge> CodesTo(string email) =>
        ReauthTestHelpers.MailsTo(_factory, email)
            .Where(m => m.Content is LoginChallengeEmail.AddressChangeCode)
            .ToList();

    private async Task<HttpResponseMessage> GetMeAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(req, ct);
    }

    private async Task<ApplicationUser> RowAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return user.ShouldNotBeNull();
    }

    private async Task<int> RequestAuditsAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries
            .AsNoTracking()
            .CountAsync(e => e.UserId == userId && e.EventType == "User.EmailChangeRequested", ct);
    }

    private static async Task<string> TitleOf(HttpResponseMessage response, CancellationToken ct) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("title").GetString()!;

    [Fact]
    public async Task POST_change_email_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new { reauthGrant = UnusableGrant, newEmail = Address("anon-new") },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_change_email_with_an_unusable_grant_returns_401_and_does_not_send()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = Address("wrong");
        var newEmail = Address("wrong-new");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        var response = await ChangeAsync(sessionId, UnusableGrant, newEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Byte-identical to the shared InvalidCredentials 401 (AuthProblem): an unusable grant reveals nothing.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
        json.GetProperty("detail").GetString().ShouldBe("E-post eller lösenord är felaktigt.");

        _factory.Emails.LoginChallenges.ShouldNotContain(m => m.ToEmail == newEmail);
        (await RowAsync(email)).Email.ShouldBe(email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_change_email_with_empty_grant_returns_400(string? reauthGrant)
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, Address("emptycur"), ct: ct);

        var response = await ChangeAsync(sessionId, reauthGrant, Address("emptycur-new"), ct);

        // ValidationBehavior runs before ReauthenticationBehavior: empty vs unusable = 400 vs 401, revealing nothing.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@no-local.se")]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_change_email_with_malformed_new_email_returns_400(string? newEmail)
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, Address("badnew"), ct: ct);

        // Validation runs before the grant is redeemed, so no grant is spent on a malformed request.
        var response = await ChangeAsync(sessionId, UnusableGrant, newEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_change_email_with_valid_input_returns_202_with_a_challenge_id_codes_the_new_address_and_leaves_the_account_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = Address("ok");
        var newEmail = Address("ok-new");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        var response = await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), newEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("challengeId").GetString().ShouldNotBeNullOrEmpty();
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["challengeId"]);

        // One code, to the NEW address, and none to the old one.
        CodesTo(newEmail).ShouldHaveSingleItem();
        CodesTo(email).ShouldBeEmpty();

        // The request step moves nothing and touches no session.
        var row = await RowAsync(email);
        row.Email.ShouldBe(email);
        row.UserName.ShouldBe(email);
        (await GetMeAsync(sessionId, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_change_email_with_taken_address_returns_409_and_does_not_send()
    {
        var ct = TestContext.Current.CancellationToken;
        var takenEmail = Address("taken");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, takenEmail, ct: ct);

        var email = Address("taker");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        var response = await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), takenEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(response, ct)).ShouldBe("Auth.EmailTaken");
        CodesTo(takenEmail).ShouldBeEmpty();
    }

    [Fact]
    public async Task POST_change_email_to_an_address_held_only_as_a_user_name_returns_409()
    {
        // The state a swap leaves when its address write fails after its user-name write
        // (UserAccountService.SwapConfirmedAddressAsync, log 4001): the address is another row's user name and
        // no row's address, and the unique index holds it there. Written here by that swap's own first call.
        var ct = TestContext.Current.CancellationToken;
        var held = Address("held-name");
        var holder = Address("holder");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, holder, ct: ct);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var row = (await users.FindByEmailAsync(holder)).ShouldNotBeNull();
            (await users.SetUserNameAsync(row, held)).Succeeded.ShouldBeTrue();
        }

        var email = Address("name-taker");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        var response = await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), held, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(response, ct)).ShouldBe("Auth.EmailTaken");
        CodesTo(held).ShouldBeEmpty();
    }

    [Fact]
    public async Task POST_change_email_to_own_current_address_returns_409_and_does_not_send()
    {
        // The address is taken by the caller, and the caller already knows their own address, so the 409 is no
        // enumeration oracle. The frontend keeps submit disabled until the address differs.
        var ct = TestContext.Current.CancellationToken;
        var email = Address("self");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        var response = await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(response, ct)).ShouldBe("Auth.EmailTaken");
        CodesTo(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_request_by_the_same_user_inside_the_window_returns_409_cooldown_and_does_not_send()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = Address("again");
        var first = Address("again-first");
        var second = Address("again-second");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        (await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), first, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await ReauthTestHelpers.LetTheCooldownLapseAsync(_factory, email, ct);
        var refused = await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), second, ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(refused, ct)).ShouldBe("Auth.ChangeEmailCooldown");
        CodesTo(second).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_user_asking_for_the_same_address_inside_the_window_gets_the_same_cooldown_answer()
    {
        // The target cooldown is keyed by the address and shared between users, and its refusal carries the
        // user cooldown's code, so it does not tell the second caller that someone just asked for the address.
        var ct = TestContext.Current.CancellationToken;
        var target = Address("shared-target");
        var firstEmail = Address("shared-a");
        var secondEmail = Address("shared-b");
        var firstSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, firstEmail, ct: ct);
        var secondSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, secondEmail, ct: ct);

        (await ChangeAsync(firstSession, await MintGrantAsync(firstSession, firstEmail, ct), target, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var refused = await ChangeAsync(secondSession, await MintGrantAsync(secondSession, secondEmail, ct), target, ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(refused, ct)).ShouldBe("Auth.ChangeEmailCooldown");
        CodesTo(target).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_new_address_gets_three_codes_a_day_whoever_asks()
    {
        // The per-address cap (security-auditor, PR 4's panel): guessing against one address is bounded however
        // many accounts ask for it. Each ask waits out the target cooldown, which THE CLOCK ends (the helper).
        var ct = TestContext.Current.CancellationToken;
        var target = Address("capped-target");

        foreach (var label in new[] { "capped-a", "capped-b", "capped-c" })
        {
            var email = Address(label);
            var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);
            (await ChangeAsync(session, await MintGrantAsync(session, email, ct), target, ct))
                .StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await ReauthTestHelpers.LetTheTargetCooldownLapseAsync(_factory, target);
        }

        var fourthEmail = Address("capped-d");
        var fourth = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, fourthEmail, ct: ct);
        var refused = await ChangeAsync(fourth, await MintGrantAsync(fourth, fourthEmail, ct), target, ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(refused, ct)).ShouldBe("Auth.ChangeEmailCooldown");
        CodesTo(target).Count.ShouldBe(3);
    }

    [Fact]
    public async Task POST_change_email_writes_User_EmailChangeRequested_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = Address("audit");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);

        (await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), Address("audit-new"), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await RowAsync(email);

        // The actor IS the authenticated user, so UserId (actor) == AggregateId (target).
        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.UserId == user.Id && e.EventType == "User.EmailChangeRequested")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exactly one User.EmailChangeRequested row per request");
        auditEntries[0].AggregateType.ShouldBe("User");
        auditEntries[0].AggregateId.ShouldBe(user.Id, "the aggregate id is the Identity user id");
    }

    // ---------------------------------------------------------------------------------------
    // #1087 — the transport half of the capability gate. AuthErrorCodes.EmailDeliveryUnavailable is carried by
    // DomainError.Validation, so a deleted AuthEndpoints arm would degrade the 503 to a 400 with the unit suite
    // green. Both halves live in ONE test: the 503 alone is compatible with a gate stuck on.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task POST_change_email_returns_503_when_the_sender_cannot_deliver_and_202_when_it_can()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = Address("nodeliver");
        var newEmail = Address("nodeliver-new");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: ct);
        var userId = (await RowAsync(email)).Id;

        // The grant is minted while the sender can deliver, and the refused request SPENDS it: the behavior redeems
        // before the handler answers 503.
        var grant = await MintGrantAsync(sessionId, email, ct);
        HttpResponseMessage refused;
        using (_factory.Emails.Incapable())
        {
            refused = await ChangeAsync(sessionId, grant, newEmail, ct);
        }

        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        // The parsed title, because a Redis outage also produces a 503 on this surface.
        (await TitleOf(refused, ct)).ShouldBe("Auth.EmailDeliveryUnavailable");
        CodesTo(newEmail).ShouldBeEmpty();
        (await RequestAuditsAsync(userId, ct)).ShouldBe(0, "a refused request leaves no audit row");

        // The crossing arm: capability restored, same user, same address. A 202 without waiting also shows that
        // the refusal began neither change-email cooldown. The second grant needs THE CLOCK to end the re-auth
        // cooldown, which the helper names.
        await ReauthTestHelpers.LetTheCooldownLapseAsync(_factory, email, ct);
        (await ChangeAsync(sessionId, await MintGrantAsync(sessionId, email, ct), newEmail, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        CodesTo(newEmail).ShouldHaveSingleItem();
        (await RequestAuditsAsync(userId, ct)).ShouldBe(1, "the accepted request writes the row the refused one withheld");
    }
}
