using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1735 — the two proof steps, POST /api/v1/auth/challenge/verify and POST /api/v1/auth/link (ADR 0142 D3),
/// driven end to end: every challenge is minted by <c>POST /auth/challenge</c> and read back from the mail
/// the dispatch consumer sent, so no test hands the routes a credential production did not mint. Each test
/// uses its own address, because the budgets are per address and the Redis container is shared.
/// </summary>
[Collection("Api")]
public class LoginChallengeProofTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"lp-{label}-{Guid.NewGuid():N}@example.se";

    private sealed record Minted(string ChallengeId, LoginChallengeEmail Mail)
    {
        public string Code => ((LoginChallengeEmail.CodeAndLink)Mail).Code.Reveal();

        public string Link => Mail switch
        {
            LoginChallengeEmail.CodeAndLink both => both.Link.Reveal(),
            LoginChallengeEmail.LinkOnly only => only.Link.Reveal(),
            _ => throw new InvalidOperationException($"A {Mail.GetType().Name} mail carries no link."),
        };
    }

    /// <summary>Requests a challenge as <paramref name="typedAs"/> and reads the mail <paramref name="email"/> got.</summary>
    private async Task<Minted> MintAsync(string email, string? typedAs = null)
    {
        var before = MailsTo(email).Count;
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email = typedAs ?? email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (MailsTo(email).Count == before)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        return new Minted(body.RootElement.GetProperty("challengeId").GetString()!, MailsTo(email)[^1].Content);
    }

    private List<RecordedLoginChallenge> MailsTo(string email) =>
        _factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList();

    private Task<HttpResponseMessage> VerifyAsync(string challengeId, string code) =>
        _client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code }, Ct);

    private Task<HttpResponseMessage> LinkAsync(string token) =>
        _client.PostAsJsonAsync("/api/v1/auth/link", new { token }, Ct);

    private static async Task<string?> TitleOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await TitleOf(response)).ShouldBe(code);
    }

    private static async Task<string> SignedInSessionOf(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("outcome").GetString().ShouldBe("signedIn");
        body.RootElement.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["outcome", "sessionId"]);
        return body.RootElement.GetProperty("sessionId").GetString()!;
    }

    private async Task<HttpStatusCode> ProbeAsync(string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await _client.SendAsync(request, Ct)).StatusCode;
    }

    private sealed record IdentityRow(
        string? PasswordHash, string? SecurityStamp, bool EmailConfirmed, int AccessFailedCount,
        DateTimeOffset? LockoutEnd);

    private async Task<IdentityRow> IdentityRowOf(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return new IdentityRow(
            user!.PasswordHash, user.SecurityStamp, user.EmailConfirmed, user.AccessFailedCount, user.LockoutEnd);
    }

    private async Task<Guid> UserIdOf(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    [Fact]
    public async Task The_mailed_code_signs_in_with_a_persistent_session()
    {
        var email = NewAddress("code");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);

        var sessionId = await SignedInSessionOf(await VerifyAsync(minted.ChallengeId, minted.Code));

        await using var scope = _factory.Services.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ISessionStore>()
            .GetAsync(SessionId.FromRaw(sessionId), Ct);
        session.ShouldNotBeNull();
        session.Lifetime.ShouldBe(SessionLifetime.Persistent);
        (await ProbeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("upper-case")]
    [InlineData("long-s")]
    public async Task A_login_typed_in_another_spelling_of_the_accounts_address_signs_its_owner_in(string spelling)
    {
        // The mail goes to the account's own spelling, and so does the record the code proves.
        var email = NewAddress("case");
        var typed = spelling == "upper-case" ? email.ToUpperInvariant() : email.Replace("-case-", "-caſe-");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

        var minted = await MintAsync(email, typedAs: typed);

        var sessionId = await SignedInSessionOf(await VerifyAsync(minted.ChallengeId, minted.Code));
        (await ProbeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        MailsTo(typed).ShouldBeEmpty();
    }

    [Fact]
    public async Task Three_wrong_codes_warn_then_burn_the_code_and_the_right_code_is_then_burned_too()
    {
        var email = NewAddress("burn");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);
        var wrong = minted.Code == "000000" ? "111111" : "000000";

        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, wrong), HttpStatusCode.BadRequest,
            AuthErrorCodes.LoginCodeWrong);
        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, wrong), HttpStatusCode.BadRequest,
            AuthErrorCodes.LoginCodeWrongLastAttempt);
        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, wrong), HttpStatusCode.Gone,
            AuthErrorCodes.LoginCodeBurned);
        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, minted.Code), HttpStatusCode.Gone,
            AuthErrorCodes.LoginCodeBurned);
    }

    [Fact]
    public async Task A_burned_code_leaves_the_link_usable()
    {
        var email = NewAddress("burn-link");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);
        var wrong = minted.Code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 3; i++)
            await VerifyAsync(minted.ChallengeId, wrong);

        await SignedInSessionOf(await LinkAsync(minted.Link));
    }

    [Fact]
    public async Task An_unknown_challenge_and_a_used_one_both_answer_expired()
    {
        var email = NewAddress("used");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);
        await SignedInSessionOf(await VerifyAsync(minted.ChallengeId, minted.Code));

        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, minted.Code), HttpStatusCode.Gone,
            AuthErrorCodes.LoginCodeExpired);
        await ShouldFailWith(await VerifyAsync(ChallengeId.Generate().Reveal(), "123456"), HttpStatusCode.Gone,
            AuthErrorCodes.LoginCodeExpired);
    }

    [Fact]
    public async Task The_link_is_single_use_and_spends_the_code_with_it()
    {
        var email = NewAddress("link-once");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);

        await SignedInSessionOf(await LinkAsync(minted.Link));

        await ShouldFailWith(await LinkAsync(minted.Link), HttpStatusCode.Gone, AuthErrorCodes.LoginLinkUnusable);
        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, minted.Code), HttpStatusCode.Gone,
            AuthErrorCodes.LoginCodeExpired);
    }

    [Fact]
    public async Task A_verified_code_spends_the_link()
    {
        var email = NewAddress("code-once");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);

        await SignedInSessionOf(await VerifyAsync(minted.ChallengeId, minted.Code));

        await ShouldFailWith(await LinkAsync(minted.Link), HttpStatusCode.Gone, AuthErrorCodes.LoginLinkUnusable);
    }

    [Fact]
    public async Task Unusable_links_and_malformed_codes_spend_no_attempt()
    {
        var email = NewAddress("no-spend");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);

        // The link's own id with a secret that is not the minted one: the store locates the record and still
        // refuses, and the attempt counter the code arm reads is untouched.
        var raw = System.Buffers.Text.Base64Url.DecodeFromChars(minted.Link);
        raw[^1] ^= 0xFF;
        var forged = System.Buffers.Text.Base64Url.EncodeToString(raw);
        for (var i = 0; i < 3; i++)
            await ShouldFailWith(await LinkAsync(forged), HttpStatusCode.Gone, AuthErrorCodes.LoginLinkUnusable);
        (await VerifyAsync(minted.ChallengeId, "12345")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Were any of those counted, this first wrong code would be the last-attempt warning or a burn.
        var wrong = minted.Code == "000000" ? "111111" : "000000";
        await ShouldFailWith(await VerifyAsync(minted.ChallengeId, wrong), HttpStatusCode.BadRequest,
            AuthErrorCodes.LoginCodeWrong);
        await SignedInSessionOf(await VerifyAsync(minted.ChallengeId, minted.Code));
    }

    [Fact]
    public async Task A_confirmed_password_account_logs_in_by_code_and_its_identity_row_is_unchanged()
    {
        var email = NewAddress("pw-confirmed");
        var existingSession = await AuthTestHelpers.RegisterWithPasswordAndGetSessionIdAsync(_factory, email, ct: Ct);
        await ConfirmThroughVerifyEmailAsync(email);
        var before = await IdentityRowOf(email);
        before.PasswordHash.ShouldNotBeNull();
        before.EmailConfirmed.ShouldBeTrue();

        var minted = await MintAsync(email);
        var wrong = minted.Code == "000000" ? "111111" : "000000";
        await VerifyAsync(minted.ChallengeId, wrong);
        await SignedInSessionOf(await VerifyAsync(minted.ChallengeId, minted.Code));

        // Wrong codes never reach lockout, and a confirmed account's password and stamp survive until 5b.
        (await IdentityRowOf(email)).ShouldBe(before);
        (await ProbeAsync(existingSession)).ShouldBe(HttpStatusCode.OK);
        await AuthTestHelpers.LoginAndGetSessionIdAsync(_client, email, ct: Ct);
    }

    [Fact]
    public async Task A_first_proof_of_an_unconfirmed_inbox_removes_the_password_and_every_earlier_session()
    {
        var email = NewAddress("pw-unconfirmed");
        var earlierSession = await AuthTestHelpers.RegisterWithPasswordAndGetSessionIdAsync(_factory, email, ct: Ct);
        var before = await IdentityRowOf(email);
        before.PasswordHash.ShouldNotBeNull();
        before.EmailConfirmed.ShouldBeFalse();
        var userId = await UserIdOf(email);

        var minted = await MintAsync(email);
        var sessionId = await SignedInSessionOf(await LinkAsync(minted.Link));

        var after = await IdentityRowOf(email);
        after.EmailConfirmed.ShouldBeTrue();
        after.PasswordHash.ShouldBeNull();
        after.SecurityStamp.ShouldNotBe(before.SecurityStamp);

        (await ProbeAsync(earlierSession)).ShouldBe(HttpStatusCode.Unauthorized);
        (await ProbeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = AuthTestHelpers.DefaultTestPassword }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        await using var scope = _factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IAppDbContext>().AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == userId && e.EventType == PasswordlessSessionGrant.InboxProvenAuditEventType)
            .CountAsync(Ct);
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task An_account_deleted_after_the_mail_went_out_gets_its_deletion_date_not_a_session()
    {
        var email = NewAddress("deleted-later");
        var session = await AuthTestHelpers.RegisterWithPasswordAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);

        using (var delete = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/delete"))
        {
            delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
            delete.Content = JsonContent.Create(new { password = AuthTestHelpers.DefaultTestPassword });
            (await _client.SendAsync(delete, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        }

        var response = await VerifyAsync(minted.ChallengeId, minted.Code);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("outcome").GetString().ShouldBe("pendingDeletion");
        // The restore window runs from the soft-delete the endpoint stamped; the date is its UTC calendar day.
        var userId = await UserIdOf(email);
        await using var scope = _factory.Services.CreateAsyncScope();
        var deletedAt = await scope.ServiceProvider.GetRequiredService<IAppDbContext>().JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(js => js.UserId == userId)
            .Select(js => js.DeletedAt)
            .SingleAsync(Ct);
        body.RootElement.GetProperty("permanentDeletionDate").GetString().ShouldBe(
            deletedAt!.Value.AddDays(30).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        body.RootElement.TryGetProperty("sessionId", out _).ShouldBeFalse();
    }

    // The address moves through the production change-email path — its token, confirmed at
    // /confirm-email-change — inside the challenge's lifetime, so the proven address has no account.
    private async Task<Minted> MintThenMoveTheAccountAwayAsync(string label)
    {
        var email = NewAddress(label);
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var minted = await MintAsync(email);
        var userId = await UserIdOf(email);
        var newEmail = NewAddress(label + "-to");
        string token;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            token = (await scope.ServiceProvider.GetRequiredService<IUserAccountService>()
                .GenerateChangeEmailTokenAsync(userId, newEmail, Ct)).Value;
        }

        (await _client.PostAsJsonAsync(
                "/api/v1/auth/confirm-email-change", new { uid = userId, email = newEmail, token }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return minted;
    }

    private static async Task<JsonElement> OkBodyOf(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task An_address_that_left_its_account_gets_registration_closed_while_registration_is_closed()
    {
        var minted = await MintThenMoveTheAccountAwayAsync("moved-closed");

        // This host has registration open. The flag moves only with a restart, which hands every reader of
        // IOptions<AuthOptions> one new value; setting that one instance is the same state. The collection runs
        // serially and the finally restores it.
        var flags = _factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        JsonElement body;
        try
        {
            flags.RegistrationsOpen = false;
            body = await OkBodyOf(await VerifyAsync(minted.ChallengeId, minted.Code));
        }
        finally
        {
            flags.RegistrationsOpen = true;
        }

        body.EnumerateObject().Select(p => p.Name).ShouldBe(["outcome"]);
        body.GetProperty("outcome").GetString().ShouldBe("registrationClosed");
    }

    [Fact]
    public async Task An_address_that_left_its_account_is_asked_for_consent_when_its_code_is_proven()
    {
        // #1737 — the code proves the inbox, the address has no account, and registration is open here.
        var minted = await MintThenMoveTheAccountAwayAsync("moved-code");

        var body = await OkBodyOf(await VerifyAsync(minted.ChallengeId, minted.Code));

        body.GetProperty("outcome").GetString().ShouldBe("consentRequired");
        body.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["grantToken", "outcome"]);
    }

    [Fact]
    public async Task An_address_that_left_its_account_is_unavailable_when_its_link_is_proven()
    {
        // #1737 — the one way a LINK is proven for an address without an account. No new account rises from
        // a bearer that has sat in a browser's history (security-auditor, 2026-09-20).
        var minted = await MintThenMoveTheAccountAwayAsync("moved-link");

        var body = await OkBodyOf(await LinkAsync(minted.Link));

        body.EnumerateObject().Select(p => p.Name).ShouldBe(["outcome"]);
        body.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
    }

    [Fact]
    public async Task A_link_request_without_a_token_of_the_minted_shape_is_a_400()
    {
        (await _client.PostAsJsonAsync("/api/v1/auth/link", new { }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        (await LinkAsync(new string('A', 129))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v1/auth/challenge")]
    [InlineData("/api/v1/auth/challenge/verify")]
    [InlineData("/api/v1/auth/challenge/complete")]
    [InlineData("/api/v1/auth/link")]
    public void Every_login_challenge_route_carries_the_auth_write_rate_limit(string route)
    {
        _ = _factory.CreateClient();

        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == route)
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            .ShouldBe(RateLimitingExtensions.AuthWritePolicy);
    }

    [Fact]
    public async Task An_unreachable_redis_answers_one_uniform_503_on_both_proof_routes()
    {
        using var _ = _factory.LoginChallengeFaults.Unavailable();

        foreach (var response in new[]
                 {
                     await VerifyAsync(ChallengeId.Generate().Reveal(), "123456"),
                     await LinkAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            body.RootElement.GetProperty("error").GetString().ShouldBe(StoreUnavailableException.ClientMessage);
        }
    }

    // The production path that confirms a password account's address: the token the confirmation mail
    // carries, presented to /verify-email.
    private async Task ConfirmThroughVerifyEmailAsync(string email)
    {
        var userId = await UserIdOf(email);
        await using var scope = _factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var token = await accounts.GenerateEmailConfirmationTokenAsync(userId, Ct);
        (await _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { uid = userId, token = token.Value }, Ct))
            .IsSuccessStatusCode.ShouldBeTrue();
    }
}
