using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1975 (ADR 0153) — the owner's side, through the public route: one byte-identical refusal for everything but a full
/// match (security-auditor T-5), the delay (T-8), the races (T-6), the account while the change waits and after it
/// (T-7), every session gone and none issued (T-9), the external logins left as they were (T-10), and the one audit row
/// with the account as its user (T-11). A change whose delay has run is written by the production adapter as it would
/// have been written 73 hours ago; the host's clock is never moved.
/// </summary>
[Collection("Api")]
public sealed class AccountEmailChangeCompletionTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _anonymous = factory.CreateClient();
    private readonly string _token = AdminAccountsKit.NewToken();

    // The external-login start budget is one key for the whole host, which this collection shares.
    public ValueTask InitializeAsync() => ResetStartBudgetAsync();

    public ValueTask DisposeAsync() => ResetStartBudgetAsync();

    private async ValueTask ResetStartBudgetAsync()
    {
        await using var admin = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        await admin.GetDatabase().KeyDeleteAsync(
            RedisRateBudget.Key(ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject));
    }

    private string Address(string label) => AdminAccountsKit.Address(_token, label);

    private sealed record Owner(Guid UserId, string Current, string SessionId);

    private async Task<Owner> OwnerAsync(string? current = null)
    {
        current ??= Address("agare");
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, current, ct: Ct);
        return new Owner(await AdminAccountsKit.UserIdAsync(factory, current, Ct), current, sessionId);
    }

    private Task<HttpResponseMessage> CompleteAsync(string current, string newEmail, string code) =>
        AccountEmailChangeKit.CompleteAsync(_anonymous, current, newEmail, code, Ct);

    private async Task<ApplicationUser> AccountAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString())).ShouldNotBeNull();
    }

    private async Task<HttpStatusCode> MeAsync(string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await _anonymous.SendAsync(request, Ct)).StatusCode;
    }

    private static async Task<string> RefusalBodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Gone, text);
        return text;
    }

    private static string WrongCodeFor(string code) => code == "000000" ? "111111" : "000000";

    // ── a completed change ──

    [Fact]
    public async Task A_completion_moves_the_account_to_the_records_spelling_logs_every_device_out_and_issues_no_session()
    {
        var owner = await OwnerAsync();
        var second = await SecondSessionAsync(owner.UserId);
        var recordsSpelling = $"Ny.Adress-{_token}@Example.se";
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(
            factory, owner.UserId, recordsSpelling, owner.Current, Ct);
        await using (var redis = await ConnectionMultiplexer.ConnectAsync(factory.DurableRedisConnectionString))
        {
            (await redis.GetDatabase().SetLengthAsync($"jobbliggaren:user:{owner.UserId}:sessions")).ShouldBe(2);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, AccountEmailChangeKit.CompletePath)
        {
            Content = JsonContent.Create(new
            {
                currentEmail = owner.Current.ToUpperInvariant(),
                newEmail = recordsSpelling.ToLowerInvariant(),
                code = change.Code.Reveal(),
            }),
        };
        request.Headers.UserAgent.ParseAdd("probe/1.0");
        var response = await _anonymous.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();

        var account = await AccountAsync(owner.UserId);
        account.Email.ShouldBe(recordsSpelling);
        account.UserName.ShouldBe(recordsSpelling);
        account.EmailConfirmed.ShouldBeTrue();

        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(second)).ShouldBe(HttpStatusCode.Unauthorized);
        await using (var redis = await ConnectionMultiplexer.ConnectAsync(factory.DurableRedisConnectionString))
        {
            (await redis.GetDatabase().SetLengthAsync($"jobbliggaren:user:{owner.UserId}:sessions")).ShouldBe(0);
        }

        factory.Emails.Sent.ShouldContain(new RecordedEmail(RecordedEmailKind.EmailChangedNotification, owner.Current));

        var row = (await AccountEmailChangeKit.AuditRowsAsync(factory, owner.UserId, Ct)).ShouldHaveSingleItem();
        row.EventType.ShouldBe("User.EmailChangedViaAdministrator");
        row.UserId.ShouldBe(owner.UserId);
        row.UserAgent.ShouldBe("probe/1.0");
        row.Payload.ShouldBeNull();

        // The account's erasure reaches the row, because the row names the account as its user.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditTrailEraser>()
                .AnonymizeUserAuditTrailAsync(owner.UserId, Ct);
        }

        var erased = (await AccountEmailChangeKit.AuditRowsAsync(factory, owner.UserId, Ct)).ShouldHaveSingleItem();
        erased.UserAgent.ShouldBeNull();
        erased.IpAddress.ShouldBeNull();
    }

    private async Task<string> SecondSessionAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<ISessionStore>()
            .CreateAsync(userId, SessionLifetime.Persistent, Ct);
        return session.Id.Reveal();
    }

    // ── T-5: one answer for everything but a full match ──

    public enum Cause
    {
        WrongCode,
        WrongCurrentAddress,
        Burned,
        Expired,
        Cancelled,
        AlreadyCompleted,
        AccountPendingDeletion,
        AccountBecameAdministrator,
        AccountMovedByItsOwner,
        AddressTakenByAnotherAccount,
    }

    [Theory]
    [InlineData(Cause.WrongCode)]
    [InlineData(Cause.WrongCurrentAddress)]
    [InlineData(Cause.Burned)]
    [InlineData(Cause.Expired)]
    [InlineData(Cause.Cancelled)]
    [InlineData(Cause.AlreadyCompleted)]
    [InlineData(Cause.AccountPendingDeletion)]
    [InlineData(Cause.AccountBecameAdministrator)]
    [InlineData(Cause.AccountMovedByItsOwner)]
    [InlineData(Cause.AddressTakenByAnotherAccount)]
    public async Task Every_refusal_is_the_one_byte_identical_410(Cause cause)
    {
        var reference = await RefusalBodyAsync(await CompleteAsync(Address("x"), Address("ingen"), "123456"));
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);
        var code = change.Code.Reveal();
        var current = owner.Current;

        switch (cause)
        {
            case Cause.WrongCode:
                code = WrongCodeFor(code);
                break;
            case Cause.WrongCurrentAddress:
                current = Address("fel");
                break;
            case Cause.Burned:
                for (var i = 0; i < 3; i++)
                    await RefusalBodyAsync(await CompleteAsync(Address($"fel{i}"), newEmail, code));
                break;
            case Cause.Expired:
                await AccountEmailChangeKit.LetTheChangeExpireAsync(factory, newEmail, Ct);
                break;
            case Cause.Cancelled:
                var admin = await AccountEmailChangeKit.AdminAsync(factory, _token, Ct);
                (await admin.Client.DeleteAsync(AccountEmailChangeKit.Path(owner.UserId), Ct)).StatusCode
                    .ShouldBe(HttpStatusCode.NoContent);
                break;
            case Cause.AlreadyCompleted:
                (await CompleteAsync(current, newEmail, code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
                break;
            case Cause.AccountPendingDeletion:
                await DeleteOwnAccountAsync(owner);
                break;
            case Cause.AccountBecameAdministrator:
                await AccountEmailChangeKit.GrantAdminAsTheRetiredSeederDidAsync(factory, owner.UserId);
                break;
            case Cause.AccountMovedByItsOwner:
                (await ReauthTestHelpers.MoveTheAddressAsync(
                    factory, factory.CreateClient(), owner.SessionId, owner.Current, Address("sjalv"), Ct))
                    .StatusCode.ShouldBe(HttpStatusCode.OK);
                break;
            case Cause.AddressTakenByAnotherAccount:
                await AdminAccountsKit.OpenActiveAsync(factory, newEmail, Ct);
                break;
        }

        var refusal = await RefusalBodyAsync(await CompleteAsync(current, newEmail, code));

        refusal.ShouldBe(reference);
        JsonDocument.Parse(refusal).RootElement.GetProperty("title").GetString()
            .ShouldBe(AuthErrorCodes.AccountEmailChangeUnusable);
        if (cause != Cause.AlreadyCompleted && cause != Cause.AccountMovedByItsOwner)
            (await AccountAsync(owner.UserId)).Email.ShouldBe(owner.Current);
    }

    private async Task DeleteOwnAccountAsync(Owner owner)
    {
        var client = factory.CreateClient();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, client, owner.SessionId, owner.Current, Ct);
        (await ReauthTestHelpers.PostAsSessionAsync(client, owner.SessionId, "/api/v1/me/delete", new { reauthGrant = grant }, Ct))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    // ── T-8: the delay ──

    [Fact]
    public async Task A_full_match_before_the_delay_is_409_with_the_earliest_instant_and_spends_nothing()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(
            factory, owner.UserId, newEmail, owner.Current, Ct, hoursAgo: 1);

        for (var i = 0; i < 4; i++)
        {
            var response = await CompleteAsync(owner.Current, newEmail, change.Code.Reveal());
            var text = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, text);
            var problem = JsonDocument.Parse(text).RootElement;
            problem.GetProperty("title").GetString().ShouldBe(AuthErrorCodes.AccountEmailChangeNotYet);
            problem.GetProperty("completableFrom").GetDateTimeOffset().ShouldBe(change.CompletableFrom);
        }

        (await AccountAsync(owner.UserId)).Email.ShouldBe(owner.Current);
        (await AccountEmailChangeKit.AuditRowsAsync(factory, owner.UserId, Ct)).ShouldBeEmpty();
    }

    // ── T-6: races ──

    [Fact]
    public async Task Two_completions_at_once_move_the_account_once()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            AccountEmailChangeKit.CompleteAsync(factory.CreateClient(), owner.Current, newEmail, change.Code.Reveal(), Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Gone).ShouldBe(1);
        (await AccountEmailChangeKit.AuditRowsAsync(factory, owner.UserId, Ct)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_completion_racing_a_cancel_lets_exactly_one_through_and_writes_one_row()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, _token, Ct);
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        var completion = AccountEmailChangeKit.CompleteAsync(factory.CreateClient(), owner.Current, newEmail, change.Code.Reveal(), Ct);
        var cancel = admin.Client.DeleteAsync(AccountEmailChangeKit.Path(owner.UserId), Ct);
        await Task.WhenAll(completion, cancel);

        var completed = (await completion).StatusCode == HttpStatusCode.NoContent;
        var cancelled = (await cancel).StatusCode == HttpStatusCode.NoContent;
        completed.ShouldNotBe(cancelled);
        (await AccountEmailChangeKit.AuditRowsAsync(factory, owner.UserId, Ct)).Select(row => row.EventType)
            .ShouldBe([completed ? "User.EmailChangedViaAdministrator" : "Admin.AccountEmailChangeCancelled"]);
    }

    // ── what fails after the swap answers 500, never a retryable 503 ──

    [Fact]
    public async Task A_teardown_that_fails_answers_500_over_the_committed_change()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        HttpResponseMessage response;
        using (factory.SessionTeardownFaults.FailingFor(owner.UserId))
            response = await CompleteAsync(owner.Current, newEmail, change.Code.Reveal());

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await AccountAsync(owner.UserId)).Email.ShouldBe(newEmail);
    }

    [Fact]
    public async Task An_audit_row_that_cannot_be_written_answers_500_after_the_teardown_ran()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        HttpResponseMessage response;
        using (factory.AuditRowSaveFailure.FailingFor("User.EmailChangedViaAdministrator", owner.UserId))
            response = await CompleteAsync(owner.Current, newEmail, change.Code.Reveal());

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await AccountAsync(owner.UserId)).Email.ShouldBe(newEmail);
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
        (await AccountEmailChangeKit.AuditRowsAsync(factory, owner.UserId, Ct)).ShouldBeEmpty();
    }

    // ── refusals decided before the store ──

    [Fact]
    public async Task A_sender_that_cannot_deliver_is_503_and_the_change_survives()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        HttpResponseMessage refused;
        using (factory.Emails.Incapable())
            refused = await CompleteAsync(owner.Current, newEmail, change.Code.Reveal());

        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CompleteAsync(owner.Current, newEmail, change.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_volatile_instance_out_of_reach_is_503_and_the_change_still_completes_after()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        using (factory.LoginChallengeFaults.Unavailable())
        {
            (await CompleteAsync(owner.Current, newEmail, change.Code.Reveal())).StatusCode
                .ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        (await AccountAsync(owner.UserId)).Email.ShouldBe(owner.Current);
        (await CompleteAsync(owner.Current, newEmail, change.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_malformed_presentation_is_400_and_spends_no_attempt()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        for (var i = 0; i < 4; i++)
        {
            (await CompleteAsync(owner.Current, newEmail, "12345")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await CompleteAsync(owner.Current, newEmail, change.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ── T-7: the account while the change waits ──

    [Fact]
    public async Task While_the_change_waits_the_current_address_logs_in_and_the_new_one_has_no_account()
    {
        var owner = await OwnerAsync();
        var newEmail = Address("ny");
        await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct, hoursAgo: 1);

        (await _anonymous.PostAsJsonAsync("/api/v1/auth/challenge", new { email = owner.Current }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);
        (await _anonymous.PostAsJsonAsync("/api/v1/auth/challenge", new { email = newEmail }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);
        await AwaitMailAsync(owner.Current);
        await AwaitMailAsync(newEmail);

        factory.Emails.LoginChallenges.Last(mail => mail.ToEmail == owner.Current).Content
            .ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();
        factory.Emails.LoginChallenges.Last(mail => mail.ToEmail == newEmail).Content
            .ShouldBeOfType<LoginChallengeEmail.NewAccountCode>();
        (await AccountAsync(owner.UserId)).Email.ShouldBe(owner.Current);
    }

    private async Task AwaitMailAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!factory.Emails.LoginChallenges.Any(m => m.ToEmail == email))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }
    }

    // ── T-10: external logins ──

    [Fact]
    public async Task A_linked_external_login_stays_linked_and_asserting_the_old_address_signs_in_nobody()
    {
        var owner = await OwnerAsync($"agare-{_token}@firma.example");
        var sub = Guid.NewGuid().ToString("N");
        (await SignInByGoogleAsync(sub, owner.Current)).GetProperty("outcome").GetString().ShouldBe("signedIn");
        var newEmail = $"ny-{_token}@firma.example";
        var change = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, newEmail, owner.Current, Ct);

        (await CompleteAsync(owner.Current, newEmail, change.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await LoginRowsAsync(owner.UserId)).ShouldBe(1);
        var stale = await SignInByGoogleAsync(sub, owner.Current);
        stale.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
        stale.TryGetProperty("sessionId", out _).ShouldBeFalse();
    }

    private async Task<JsonElement> SignInByGoogleAsync(string sub, string address)
    {
        var started = await _anonymous.PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next = "/oversikt" }, Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await started.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var state = body.GetProperty("state").GetString()!;
        var query = HttpUtility.ParseQueryString(new Uri(body.GetProperty("authorizeUrl").GetString()!).Query);

        var code = $"4/0AVGzR1{Guid.NewGuid():N}";
        factory.Google.Expect(
            code,
            GoogleUserInfoShapes.Workspace(sub, address, hostedDomain: "firma.example"),
            query["code_challenge"],
            query["redirect_uri"]!);

        var callback = await _anonymous.PostAsJsonAsync("/api/v1/auth/oauth/google/callback", new { code, state }, Ct);
        callback.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await callback.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<int> LoginRowsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .CountAsync(login => login.UserId == userId, Ct);
    }

    // ── the route's own wiring ──

    [Fact]
    public void The_route_is_public_and_takes_the_auth_write_bucket()
    {
        _ = factory.CreateClient();
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == AccountEmailChangeKit.CompletePath);

        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().ShouldBeNull();
        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.AuthWritePolicy);
    }
}
