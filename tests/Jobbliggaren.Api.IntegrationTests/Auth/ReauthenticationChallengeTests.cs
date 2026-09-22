using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1739 — the re-authentication challenge end to end (ADR 0142 D5): POST /api/v1/auth/reauth and
/// POST /api/v1/auth/reauth/verify, driven as the frontend will drive them. Every code is minted by production
/// and read back from the mail the account's own address got; every grant is presented to the operation it
/// unlocks. Each test uses its own account, because the cooldown and the code budget are per user and the
/// Redis container is shared.
/// </summary>
[Collection("Api")]
public class ReauthenticationChallengeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"reauth-{label}-{Guid.NewGuid():N}@example.se";

    private Task<string> SignedInAsync(string email) =>
        AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

    private async Task<HttpResponseMessage> RequestAsync(string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/reauth");
        if (sessionId is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> VerifyAsync(string sessionId, string? challengeId, string? code) =>
        ReauthTestHelpers.VerifyAsync(_client, sessionId, challengeId, code, Ct);

    private async Task<HttpResponseMessage> DeleteAsync(string sessionId, string grant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/delete")
        {
            Content = JsonContent.Create(new { reauthGrant = grant }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(request, Ct);
    }

    private static async Task<string?> TitleOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    [Fact]
    public async Task The_code_goes_to_the_accounts_own_address_and_the_answer_is_the_challenge_id_alone()
    {
        var email = NewAddress("mail");
        var session = await SignedInAsync(email);

        var response = await RequestAsync(session);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["challengeId"]);
        var mail = ReauthTestHelpers.MailsTo(_factory, email).ShouldHaveSingleItem().Content
            .ShouldBeOfType<LoginChallengeEmail.ReauthenticationCode>();
        mail.Code.Reveal().Length.ShouldBe(LoginChallengePolicy.CodeLength);
    }

    [Fact]
    public async Task A_verified_code_is_a_grant_that_unlocks_the_operation_once()
    {
        var email = NewAddress("grant");
        var session = await SignedInAsync(email);
        var (challengeId, code) = await ReauthTestHelpers.RequestCodeAsync(_factory, _client, session, email, Ct);

        var verified = await VerifyAsync(session, challengeId, code);

        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await verified.Content.ReadAsStringAsync(Ct));
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["reauthGrant"]);
        body.RootElement.TryGetProperty("sessionId", out _).ShouldBeFalse("a re-authentication is never a session");
        var grant = body.RootElement.GetProperty("reauthGrant").GetString()!;

        // The code is spent by the verify: a second presentation finds no challenge.
        (await VerifyAsync(session, challengeId, code)).StatusCode.ShouldBe(HttpStatusCode.Gone);

        // The grant unlocks the operation, once.
        (await DeleteAsync(session, grant)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/api/v1/auth/reauth")]
    [InlineData("/api/v1/auth/reauth/verify")]
    public void Both_routes_carry_authorization_and_the_auth_write_rate_limit_as_metadata(string route)
    {
        // The command is IAuthenticatedRequest, so AuthorizationBehavior answers 401 too; the route-level
        // requirement is pinned on the endpoint's metadata because a marker interface is not authentication
        // (the CTO's PR 4 item 24), and a status-code test cannot tell the two 401s apart.
        _ = _factory.CreateClient();

        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == route)
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldNotBeNull();
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>().ShouldNotBeNull()
            .PolicyName.ShouldBe(RateLimitingExtensions.AuthWritePolicy);
    }

    [Fact]
    public async Task Both_routes_require_a_session()
    {
        (await RequestAsync(null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync("/api/v1/auth/reauth/verify", new { challengeId = "x", code = "042917" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_code_is_400_the_third_wrong_one_burns_and_the_right_one_is_then_gone()
    {
        var email = NewAddress("burn");
        var session = await SignedInAsync(email);
        var (challengeId, code) = await ReauthTestHelpers.RequestCodeAsync(_factory, _client, session, email, Ct);
        var wrong = code == "000000" ? "000001" : "000000";

        var first = await VerifyAsync(session, challengeId, wrong);
        var second = await VerifyAsync(session, challengeId, wrong);
        var third = await VerifyAsync(session, challengeId, wrong);
        var right = await VerifyAsync(session, challengeId, code);

        first.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TitleOf(first)).ShouldBe("Auth.LoginCodeWrong");
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TitleOf(second)).ShouldBe("Auth.LoginCodeWrongLastAttempt");
        third.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await TitleOf(third)).ShouldBe("Auth.LoginCodeBurned");
        right.StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task A_malformed_code_is_refused_by_validation_and_spends_no_attempt()
    {
        var email = NewAddress("malformed");
        var session = await SignedInAsync(email);
        var (challengeId, code) = await ReauthTestHelpers.RequestCodeAsync(_factory, _client, session, email, Ct);

        foreach (var malformed in new[] { "12345", "1234567", "abcdef", "" })
            (await VerifyAsync(session, challengeId, malformed)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        foreach (var _ in Enumerable.Range(0, LoginChallengePolicy.MaxAttempts - 1))
            (await VerifyAsync(session, challengeId, code == "000000" ? "000001" : "000000"))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Two real misses, four malformed presentations: the third real attempt still verifies.
        (await VerifyAsync(session, challengeId, code)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_users_code_is_answered_as_expired_and_spends_the_owners_attempt()
    {
        // The store asserts the binding (ADR 0142 D5): a session presenting another account's challenge id and
        // code is told the challenge is gone, never that the code was wrong (Wrong carries the owner's remaining
        // attempts). The attempt is spent all the same, so the owner sees one fewer.
        var ownerEmail = NewAddress("owner");
        var owner = await SignedInAsync(ownerEmail);
        var stranger = await SignedInAsync(NewAddress("stranger"));
        var (challengeId, code) = await ReauthTestHelpers.RequestCodeAsync(_factory, _client, owner, ownerEmail, Ct);

        var foreign = await VerifyAsync(stranger, challengeId, code);
        var ownersMiss = await VerifyAsync(owner, challengeId, code == "000000" ? "000001" : "000000");

        foreign.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await TitleOf(foreign)).ShouldBe("Auth.LoginCodeExpired");
        // The stranger's presentation was the first attempt, the owner's miss the second: one left.
        (await TitleOf(ownersMiss)).ShouldBe("Auth.LoginCodeWrongLastAttempt");
    }

    [Fact]
    public async Task A_login_challenge_for_the_same_address_is_neither_burned_by_nor_usable_as_a_re_authentication()
    {
        // The two families do not see each other (ADR 0142 D1/D5): an anonymous POST /auth/challenge for the
        // account's address cannot burn the owner's re-authentication, and its code cannot be presented as one.
        var email = NewAddress("cross");
        var session = await SignedInAsync(email);
        var (reauthId, reauthCode) = await ReauthTestHelpers.RequestCodeAsync(_factory, _client, session, email, Ct);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync(Ct));
        var loginId = loginBody.RootElement.GetProperty("challengeId").GetString()!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ReauthTestHelpers.MailsTo(_factory, email).Count < 2)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the login mail");
            await Task.Delay(25, Ct);
        }
        var loginCode = ReauthTestHelpers.MailsTo(_factory, email)[^1].Content
            .ShouldBeOfType<LoginChallengeEmail.CodeAndLink>().Code.Reveal();

        // The login code presented as a re-authentication: gone, and no grant.
        (await VerifyAsync(session, loginId, loginCode)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        // The re-auth code presented as a login: gone, and no session.
        (await _client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId = reauthId, code = reauthCode }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Gone);
        // The owner's re-authentication is untouched by the login mint: it still verifies.
        (await VerifyAsync(session, reauthId, reauthCode)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_second_request_inside_the_cooldown_is_a_visible_409_and_mints_nothing()
    {
        var email = NewAddress("cooldown");
        var session = await SignedInAsync(email);
        (await RequestAsync(session)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var second = await RequestAsync(session);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(second)).ShouldBe("Auth.ReauthCooldown");
        ReauthTestHelpers.MailsTo(_factory, email).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_request_over_the_shared_mail_budget_answers_the_cooldowns_error_not_its_own()
    {
        // Three login mails spend the address's shared mail budget; the re-authentication then answers the SAME
        // 409 as its own cooldown would, so a hijacked session cannot tell that login mails were just requested.
        var email = NewAddress("mailbudget");
        var session = await SignedInAsync(email);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            // The login cooldown is per address too, so the three mints are spaced by letting the clock end it.
            (await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await LetTheLoginCooldownLapseAsync(email);
        }

        var refused = await RequestAsync(session);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TitleOf(refused)).ShouldBe("Auth.ReauthCooldown");
    }

    [Fact]
    public async Task The_sender_that_cannot_deliver_refuses_first_with_503_and_spends_no_budget()
    {
        var email = NewAddress("nodeliver");
        var session = await SignedInAsync(email);

        HttpResponseMessage refused;
        using (_factory.Emails.Incapable())
        {
            refused = await RequestAsync(session);
        }

        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await TitleOf(refused)).ShouldBe("Auth.EmailDeliveryUnavailable");
        // Nothing was spent: the same user's next request, capability restored, is admitted at once.
        (await RequestAsync(session)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Redis_holds_the_bound_record_and_never_the_challenge_id_the_code_or_the_address()
    {
        var email = NewAddress("reader");
        var session = await SignedInAsync(email);
        var (challengeId, code) = await ReauthTestHelpers.RequestCodeAsync(_factory, _client, session, email, Ct);

        await using var redis = await ConnectionMultiplexer.ConnectAsync(_factory.VolatileRedisConnectionString);
        var server = redis.GetServer(redis.GetEndPoints().Single());
        var keys = server.Keys(pattern: "jobbliggaren:auth/challenge-bound/*").Select(k => k.ToString()).ToList();

        keys.ShouldContain(RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(ChallengeId.FromRaw(challengeId))));
        keys.ShouldAllBe(k => !k.Contains(challengeId, StringComparison.Ordinal));
        // The record's fields as the bytes Redis holds, read the way RedisGrantStoreTests reads a grant.
        var fields = new List<string>();
        foreach (var key in keys)
        {
            foreach (var entry in await redis.GetDatabase().HashGetAllAsync(key))
                fields.Add($"{entry.Name}={Encoding.Latin1.GetString((byte[])entry.Value!)}");
        }

        fields.ShouldNotBeEmpty();
        var dump = string.Join('\n', keys) + '\n' + string.Join('\n', fields);
        dump.ShouldNotContain(challengeId);
        dump.ShouldNotContain(code);
        dump.ShouldNotContain(email);
    }

    // What THE CLOCK does to the address's login cooldown after its window (named per CLAUDE.md §5 Tests:).
    private async Task LetTheLoginCooldownLapseAsync(string email)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_factory.VolatileRedisConnectionString);
        var key = Jobbliggaren.Infrastructure.Auth.RedisRateBudget.Key(LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60)), email);
        (await redis.GetDatabase().KeyDeleteAsync(key)).ShouldBeTrue();
    }
}
