using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1737 — POST /api/v1/auth/challenge/complete (ADR 0142 D3), end to end on the shared host, whose registration
/// is OPEN: a new address asks for a challenge, reads its code out of the mail the consumer sent, proves it, and
/// completes with the grant it was handed. Every grant here is one production issued.
/// </summary>
[Collection("Api")]
public class LoginChallengeCompleteTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"lcc-{label}-{Guid.NewGuid():N}@example.se";

    private List<RecordedLoginChallenge> MailsTo(string email) =>
        _factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList();

    /// <summary>Requests a challenge for a NEW address, proves its mailed code, and returns the grant.</summary>
    private async Task<string> GrantForAsync(string email)
    {
        var requested = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var challengeId = (await requested.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("challengeId").GetString()!;

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (MailsTo(email).Count == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        var mail = MailsTo(email).Single().Content.ShouldBeOfType<LoginChallengeEmail.NewAccountCode>();
        var verified = await _client.PostAsJsonAsync(
            "/api/v1/auth/challenge/verify", new { challengeId, code = mail.Code.Reveal() }, Ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await verified.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().ShouldBe("consentRequired");
        return body.GetProperty("grantToken").GetString()!;
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, string? grantToken, bool acceptTerms) =>
        client.PostAsJsonAsync("/api/v1/auth/challenge/complete", new { grantToken, acceptTerms }, Ct);

    private Task<HttpResponseMessage> CompleteAsync(string? grantToken, bool acceptTerms = true) =>
        CompleteAsync(_client, grantToken, acceptTerms);

    private async Task<ApplicationUser?> UserOfAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    // The problem body minus the per-request trace id, so two refusals can be compared whole.
    private static async Task<string> ComparableAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (int)response.StatusCode + "|" + string.Join(
            "|", json.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
    }

    [Fact]
    public async Task A_new_address_that_accepts_the_terms_gets_a_passwordless_account_and_a_persistent_session()
    {
        var email = NewAddress("new");
        var grant = await GrantForAsync(email);

        var response = await CompleteAsync(grant);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().ShouldBe("signedIn");
        var sessionId = body.GetProperty("sessionId").GetString()!;

        var user = (await UserOfAsync(email)).ShouldNotBeNull();
        user.EmailConfirmed.ShouldBeTrue();
        user.PasswordHash.ShouldBeNull();
        user.UserName.ShouldBe(email);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == user.Id, Ct);
        profile.DisplayName.ShouldBeNull();
        profile.TermsAcceptance.ShouldNotBeNull();
        profile.TermsAcceptance.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);

        (await db.AuditLogEntries.AsNoTracking().CountAsync(
            e => e.AggregateId == user.Id
                && e.EventType == CompleteLoginChallengeCommandHandler.AccountCreatedAuditEventType, Ct)).ShouldBe(1);

        // Born confirmed: the grant that follows finds nothing to prove, so it writes no first-proof row.
        (await db.AuditLogEntries.AsNoTracking().CountAsync(
            e => e.AggregateId == user.Id
                && e.EventType == PasswordlessSessionGrant.InboxProvenAuditEventType, Ct)).ShouldBe(0);

        var session = await scope.ServiceProvider.GetRequiredService<ISessionStore>()
            .GetAsync(SessionId.FromRaw(sessionId), Ct);
        session.ShouldNotBeNull().Lifetime.ShouldBe(SessionLifetime.Persistent);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/profile");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        (await _client.SendAsync(me, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_the_terms_nothing_is_created_and_the_grant_is_still_usable()
    {
        var email = NewAddress("no-terms");
        var grant = await GrantForAsync(email);

        (await CompleteAsync(grant, acceptTerms: false)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await UserOfAsync(email)).ShouldBeNull();

        (await CompleteAsync(grant)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UserOfAsync(email)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_replayed_grant_answers_exactly_like_a_grant_that_never_existed()
    {
        var email = NewAddress("replay");
        var grant = await GrantForAsync(email);
        (await CompleteAsync(grant)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var replay = await CompleteAsync(grant);
        var unknown = await CompleteAsync(GrantToken.Generate().Reveal());

        replay.StatusCode.ShouldBe(HttpStatusCode.Gone);
        var replayed = await ComparableAsync(replay);
        replayed.ShouldBe(await ComparableAsync(unknown));
        replayed.ShouldContain(AuthErrorCodes.LoginGrantUnusable);
    }

    [Fact]
    public async Task With_registration_closed_even_a_live_grant_creates_nothing()
    {
        // The grant is issued by the OPEN host and presented to the CLOSED one. The switch is the handler's first
        // statement, so the closed host refuses before it would even fail to open another host's payload.
        var email = NewAddress("closed");
        var grant = await GrantForAsync(email);

        var response = await CompleteAsync(_factory.CreateRegistrationsClosedClient(), grant, acceptTerms: true);

        // 503 with the registration title (ADR 0083), not the store's uniform 503: that one carries no title.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("title").GetString().ShouldBe(AuthErrorCodes.RegistrationsClosed);
        (await UserOfAsync(email)).ShouldBeNull();

        // The closed host never reached the grant store or the claim: the same grant still completes here.
        (await CompleteAsync(grant)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_address_registered_while_the_grant_was_live_is_signed_in_to_that_account_and_not_duplicated()
    {
        var email = NewAddress("meanwhile");
        var grant = await GrantForAsync(email);
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var existing = (await UserOfAsync(email)).ShouldNotBeNull();

        var response = await CompleteAsync(grant);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("outcome").GetString().ShouldBe("signedIn");
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.Users.CountAsync(u => u.NormalizedEmail == existing.NormalizedEmail, Ct)).ShouldBe(1);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogEntries.AsNoTracking().CountAsync(
            e => e.AggregateId == existing.Id
                && e.EventType == CompleteLoginChallengeCommandHandler.AccountCreatedAuditEventType, Ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_missing_grant_is_a_validation_failure(string? grantToken) =>
        (await CompleteAsync(grantToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task A_grant_token_longer_than_any_minted_one_is_a_validation_failure() =>
        (await CompleteAsync(new string('A', 65))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task An_unreachable_redis_answers_the_uniform_503()
    {
        using var _ = _factory.LoginChallengeFaults.Unavailable();

        var response = await CompleteAsync(GrantToken.Generate().Reveal());

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("error").GetString().ShouldBe(StoreUnavailableException.ClientMessage);
    }
}
