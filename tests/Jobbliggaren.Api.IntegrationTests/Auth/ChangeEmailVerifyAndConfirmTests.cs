using System.Net;
using System.Net.Http.Headers;
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
/// The change-email VERIFY and CONFIRM steps end to end (#1739, ADR 0142 D5): the code mailed to the new address
/// yields a change-email grant bound to the user and that address, and the grant moves the account. Every code and
/// grant here is minted by production through <see cref="ReauthTestHelpers"/>. The refusals pinned are the
/// takeover directions D5 forbids: a login or re-authentication proof used as a change-email proof, one user's
/// proof in another's session, and a grant presented for another address.
/// </summary>
[Collection("Api")]
public class ChangeEmailVerifyAndConfirmTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Address(string label) => $"cev-{label}-{Guid.NewGuid():N}@example.se";

    private Task<string> SignUpAsync(string email) => AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

    private Task<string> GrantAsync(string sessionId, string email, string newEmail) =>
        ReauthTestHelpers.MintChangeEmailGrantAsync(_factory, _client, sessionId, email, newEmail, Ct);

    private Task<HttpResponseMessage> ConfirmAsync(string sessionId, string? grant, string? newEmail) =>
        ReauthTestHelpers.ConfirmAddressChangeAsync(_client, sessionId, grant, newEmail, Ct);

    private async Task<HttpStatusCode> ProbeAsync(string sessionId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await _client.SendAsync(req, Ct)).StatusCode;
    }

    private async Task<ApplicationUser> RowAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString());
        return user.ShouldNotBeNull();
    }

    private async Task<Guid> UserIdOf(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return user.ShouldNotBeNull().Id;
    }

    private async Task<string> AnotherDeviceAsync(Guid userId, SessionLifetime lifetime)
    {
        using var scope = _factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<ISessionStore>().CreateAsync(userId, lifetime, Ct);
        return session.Id.Reveal();
    }

    private static async Task<(HttpStatusCode Status, string Title)> ProblemOf(HttpResponseMessage response) =>
        (response.StatusCode,
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()!);

    [Fact]
    public async Task The_whole_journey_moves_the_account_logs_out_everywhere_and_reissues_this_device()
    {
        var oldEmail = Address("ok");
        var newEmail = Address("ok-new");
        var deviceA = await SignUpAsync(oldEmail);
        var userId = await UserIdOf(oldEmail);
        var deviceB = await AnotherDeviceAsync(userId, SessionLifetime.Persistent);

        var response = await ConfirmAsync(deviceA, await GrantAsync(deviceA, oldEmail, newEmail), newEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var reissued = body.GetProperty("sessionId").GetString().ShouldNotBeNull();
        body.GetProperty("persistent").GetBoolean().ShouldBeTrue("the confirming device's lifetime is kept");

        var row = await RowAsync(userId);
        row.Email.ShouldBe(newEmail);
        row.UserName.ShouldBe(newEmail);
        row.EmailConfirmed.ShouldBeTrue();

        (await ProbeAsync(deviceA)).ShouldBe(HttpStatusCode.Unauthorized);
        (await ProbeAsync(deviceB)).ShouldBe(HttpStatusCode.Unauthorized);
        (await ProbeAsync(reissued)).ShouldBe(HttpStatusCode.OK);

        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == oldEmail && e.Kind == RecordedEmailKind.EmailChangedNotification);
        _factory.Emails.Sent.ShouldNotContain(e =>
            e.ToEmail == newEmail && e.Kind == RecordedEmailKind.EmailChangedNotification);
    }

    [Fact]
    public async Task A_session_lifetime_device_is_reissued_a_session_lifetime_session()
    {
        var oldEmail = Address("lifetime");
        var newEmail = Address("lifetime-new");
        await SignUpAsync(oldEmail);
        var device = await AnotherDeviceAsync(await UserIdOf(oldEmail), SessionLifetime.Session);

        var response = await ConfirmAsync(device, await GrantAsync(device, oldEmail, newEmail), newEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("persistent").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_confirmed_change_writes_one_User_EmailChanged_audit_row_with_the_user_as_actor()
    {
        var oldEmail = Address("audit");
        var newEmail = Address("audit-new");
        var sessionId = await SignUpAsync(oldEmail);
        var userId = await UserIdOf(oldEmail);

        (await ConfirmAsync(sessionId, await GrantAsync(sessionId, oldEmail, newEmail), newEmail))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == userId && e.EventType == "User.EmailChanged")
            .ToListAsync(Ct);

        var entry = entries.ShouldHaveSingleItem();
        entry.AggregateType.ShouldBe("User");
        entry.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_issues_no_grant()
    {
        var email = Address("wrong");
        var newEmail = Address("wrong-new");
        var sessionId = await SignUpAsync(email);
        var (challengeId, code) =
            await ReauthTestHelpers.RequestAddressChangeAsync(_factory, _client, sessionId, email, newEmail, Ct);
        var wrong = code == "000000" ? "000001" : "000000";

        var response = await ReauthTestHelpers.VerifyAddressChangeAsync(_client, sessionId, challengeId, wrong, Ct);

        (await ProblemOf(response)).ShouldBe((HttpStatusCode.BadRequest, "Auth.LoginCodeWrong"));
    }

    [Fact]
    public async Task A_verified_code_cannot_be_verified_twice()
    {
        var email = Address("twice");
        var newEmail = Address("twice-new");
        var sessionId = await SignUpAsync(email);
        var (challengeId, code) =
            await ReauthTestHelpers.RequestAddressChangeAsync(_factory, _client, sessionId, email, newEmail, Ct);

        (await ReauthTestHelpers.VerifyAddressChangeAsync(_client, sessionId, challengeId, code, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await ReauthTestHelpers.VerifyAddressChangeAsync(_client, sessionId, challengeId, code, Ct);

        again.StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task A_reauthentication_challenge_is_no_change_email_proof_and_the_reverse()
    {
        // The takeover direction: a code proven in the CURRENT inbox must never stand in for the new inbox, and a
        // code proven in the new inbox must never re-authenticate. The store answers both as missing.
        var email = Address("purpose");
        var newEmail = Address("purpose-new");
        var sessionId = await SignUpAsync(email);

        var (reauthChallenge, reauthCode) =
            await ReauthTestHelpers.RequestCodeAsync(_factory, _client, sessionId, email, Ct);
        var asChangeEmail =
            await ReauthTestHelpers.VerifyAddressChangeAsync(_client, sessionId, reauthChallenge, reauthCode, Ct);
        (await ProblemOf(asChangeEmail)).ShouldBe((HttpStatusCode.Gone, "Auth.LoginCodeExpired"));

        await ReauthTestHelpers.LetTheCooldownLapseAsync(_factory, email, Ct);
        var (changeChallenge, changeCode) =
            await ReauthTestHelpers.RequestAddressChangeAsync(_factory, _client, sessionId, email, newEmail, Ct);
        var asReauth = await ReauthTestHelpers.VerifyAsync(_client, sessionId, changeChallenge, changeCode, Ct);
        (await ProblemOf(asReauth)).ShouldBe((HttpStatusCode.Gone, "Auth.LoginCodeExpired"));
    }

    [Fact]
    public async Task Another_users_session_cannot_verify_the_code()
    {
        var ownerEmail = Address("owner");
        var newEmail = Address("owner-new");
        var owner = await SignUpAsync(ownerEmail);
        var stranger = await SignUpAsync(Address("stranger"));
        var (challengeId, code) =
            await ReauthTestHelpers.RequestAddressChangeAsync(_factory, _client, owner, ownerEmail, newEmail, Ct);

        var response = await ReauthTestHelpers.VerifyAddressChangeAsync(_client, stranger, challengeId, code, Ct);

        (await ProblemOf(response)).ShouldBe((HttpStatusCode.Gone, "Auth.LoginCodeExpired"));
    }

    [Fact]
    public async Task A_grant_presented_for_another_address_moves_nothing_and_is_spent()
    {
        var email = Address("other-address");
        var proven = Address("other-address-proven");
        var sessionId = await SignUpAsync(email);
        var userId = await UserIdOf(email);
        var grant = await GrantAsync(sessionId, email, proven);

        var elsewhere = await ConfirmAsync(sessionId, grant, Address("other-address-typed"));
        var retry = await ConfirmAsync(sessionId, grant, proven);

        (await ProblemOf(elsewhere)).ShouldBe((HttpStatusCode.Gone, "Auth.EmailChangeGrantUnusable"));
        (await ProblemOf(retry)).ShouldBe((HttpStatusCode.Gone, "Auth.EmailChangeGrantUnusable"));
        (await RowAsync(userId)).Email.ShouldBe(email);
        (await ProbeAsync(sessionId)).ShouldBe(HttpStatusCode.OK, "a refused confirm touches no session");
    }

    [Fact]
    public async Task One_users_grant_in_another_users_session_moves_nothing()
    {
        var ownerEmail = Address("grant-owner");
        var newEmail = Address("grant-owner-new");
        var owner = await SignUpAsync(ownerEmail);
        var strangerEmail = Address("grant-stranger");
        var stranger = await SignUpAsync(strangerEmail);
        var grant = await GrantAsync(owner, ownerEmail, newEmail);

        var response = await ConfirmAsync(stranger, grant, newEmail);

        (await ProblemOf(response)).ShouldBe((HttpStatusCode.Gone, "Auth.EmailChangeGrantUnusable"));
        (await RowAsync(await UserIdOf(strangerEmail))).Email.ShouldBe(strangerEmail);
        (await RowAsync(await UserIdOf(ownerEmail))).Email.ShouldBe(ownerEmail);
    }

    [Fact]
    public async Task A_reauthentication_grant_is_no_change_email_grant()
    {
        var email = Address("reauth-grant");
        var newEmail = Address("reauth-grant-new");
        var sessionId = await SignUpAsync(email);
        var reauthGrant = await ReauthTestHelpers.MintGrantAsync(_factory, _client, sessionId, email, Ct);

        var response = await ConfirmAsync(sessionId, reauthGrant, newEmail);

        (await ProblemOf(response)).ShouldBe((HttpStatusCode.Gone, "Auth.EmailChangeGrantUnusable"));
        (await RowAsync(await UserIdOf(email))).Email.ShouldBe(email);
    }

    [Fact]
    public async Task A_grant_cannot_be_used_twice()
    {
        var email = Address("replay");
        var newEmail = Address("replay-new");
        var sessionId = await SignUpAsync(email);
        var grant = await GrantAsync(sessionId, email, newEmail);

        var first = await ConfirmAsync(sessionId, grant, newEmail);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reissued = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("sessionId").GetString()!;

        var second = await ConfirmAsync(reissued, grant, newEmail);

        (await ProblemOf(second)).ShouldBe((HttpStatusCode.Gone, "Auth.EmailChangeGrantUnusable"));
    }

    [Fact]
    public async Task An_address_taken_between_verify_and_confirm_is_refused_and_moves_nothing()
    {
        var email = Address("toctou");
        var contested = Address("toctou-taken");
        var sessionId = await SignUpAsync(email);
        var userId = await UserIdOf(email);
        var grant = await GrantAsync(sessionId, email, contested);

        await SignUpAsync(contested);
        var response = await ConfirmAsync(sessionId, grant, contested);

        (await ProblemOf(response)).ShouldBe((HttpStatusCode.Conflict, "Auth.EmailTaken"));
        var row = await RowAsync(userId);
        row.Email.ShouldBe(email);
        row.UserName.ShouldBe(email);
    }

    [Fact]
    public async Task A_retry_after_the_callers_own_half_finished_swap_completes_it()
    {
        // The state a swap leaves when its address write fails after its user-name write
        // (UserAccountService.SwapConfirmedAddressAsync, log 4001), written here by that swap's own first call: the
        // new address is this row's user name and not yet its address. It holds the address against everyone else,
        // and against the caller not at all.
        var email = Address("half");
        var newEmail = Address("half-new");
        var sessionId = await SignUpAsync(email);
        var userId = await UserIdOf(email);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var row = (await users.FindByIdAsync(userId.ToString())).ShouldNotBeNull();
            (await users.SetUserNameAsync(row, newEmail)).Succeeded.ShouldBeTrue();
        }

        var response = await ConfirmAsync(sessionId, await GrantAsync(sessionId, email, newEmail), newEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = await RowAsync(userId);
        moved.Email.ShouldBe(newEmail);
        moved.UserName.ShouldBe(newEmail);
    }

    [Theory]
    [InlineData("/api/v1/auth/change-email/verify")]
    [InlineData("/api/v1/auth/change-email/confirm")]
    public async Task Both_steps_refuse_a_request_without_a_session(string path)
    {
        var response = await _client.PostAsJsonAsync(
            path, new { challengeId = "AAECAwQFBgcICQoLDA0ODw", code = "042917", changeEmailGrant = "x", newEmail = "a@b.se" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_public_token_route_is_retired()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/confirm-email-change",
            new { uid = Guid.NewGuid(), email = Address("retired"), token = "dG9rZW4" },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
