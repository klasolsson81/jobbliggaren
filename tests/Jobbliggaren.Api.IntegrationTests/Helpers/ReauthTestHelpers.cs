using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

/// <summary>
/// #1739 — mints a re-authentication grant the way production does (ADR 0142 D5): the signed-in session asks
/// for a code, the code is read back from the mail the recording sender got, and the code is presented. No
/// test hands a route a grant production did not mint. Each user can mint once per cooldown window (60 s) and
/// three mails per 10 minutes, so a test that needs several grants uses several users.
/// </summary>
internal static class ReauthTestHelpers
{
    /// <summary>Requests a re-authentication code for the session and reads the challenge id and the code.</summary>
    public static async Task<(string ChallengeId, string Code)> RequestCodeAsync(
        ApiFactory factory, HttpClient client, string sessionId, string email, CancellationToken ct)
    {
        var before = MailsTo(factory, email).Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/reauth");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        var response = await client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(ct));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var challengeId = body.RootElement.GetProperty("challengeId").GetString()!;

        // The send is synchronous on this route, so the mail is there when the answer is.
        var mails = MailsTo(factory, email);
        mails.Count.ShouldBe(before + 1, "the request path sends exactly one mail before it answers");
        var code = mails[^1].Content.ShouldBeOfType<LoginChallengeEmail.ReauthenticationCode>().Code.Reveal();
        return (challengeId, code);
    }

    /// <summary>Presents a code for the session and returns the raw response.</summary>
    public static async Task<HttpResponseMessage> VerifyAsync(
        HttpClient client, string sessionId, string? challengeId, string? code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/reauth/verify")
        {
            Content = JsonContent.Create(new { challengeId, code }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await client.SendAsync(request, ct);
    }

    /// <summary>Request, read the mail, verify: the grant a sensitive operation carries.</summary>
    public static async Task<string> MintGrantAsync(
        ApiFactory factory, HttpClient client, string sessionId, string email, CancellationToken ct)
    {
        var (challengeId, code) = await RequestCodeAsync(factory, client, sessionId, email, ct);
        var verified = await VerifyAsync(client, sessionId, challengeId, code, ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(ct));

        using var body = JsonDocument.Parse(await verified.Content.ReadAsStringAsync(ct));
        return body.RootElement.GetProperty("reauthGrant").GetString()!;
    }

    internal static List<RecordedLoginChallenge> MailsTo(ApiFactory factory, string email) =>
        factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList();

    /// <summary>
    /// What THE CLOCK does to the user's re-authentication cooldown after its window: the key expires. Named
    /// per CLAUDE.md §5 <c>Tests:</c> — no path in <c>src/</c> ends a cooldown early, and a test that needs a
    /// second grant for one user inside the window stands in for the sixty seconds it would otherwise wait.
    /// The key is the adapter's own (<see cref="RedisRateBudget.Key"/>), so a renamed scope fails here.
    /// </summary>
    public static async Task LetTheCooldownLapseAsync(ApiFactory factory, string email, CancellationToken ct)
    {
        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await users.FindByEmailAsync(email)).ShouldNotBeNull().Id;
        }

        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var key = RedisRateBudget.Key(LoginChallengePolicy.ReauthCooldown(TimeSpan.FromSeconds(60)), userId.ToString());
        (await redis.GetDatabase().KeyDeleteAsync(key)).ShouldBeTrue("the cooldown key the request path wrote");
        _ = ct;
    }
}
