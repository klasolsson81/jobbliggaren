using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1735 — WHICH INSTANCE each key class lands on, through the production entry point. The shared host runs
/// two Redis containers for this test alone: the durable one and the deploy stack's own
/// <c>redis-volatile</c>. One instance behind both keys cannot tell a moved store from an unmoved one, which
/// is why the hosts that reuse one container assert nothing about placement and this class does.
///
/// <para>
/// The sentence this makes true is the closed-registration mail's
/// (<c>EmailTemplates.LoginChallenge.cs</c>): the address is kept protected for the record's lifetime and a
/// fingerprint of it for at most a day, and afterwards it is not with us. On the durable instance an expired
/// key stays in the append-only file until the next rewrite, so the sentence holds only if no key of these
/// classes is ever written there.
/// </para>
/// </summary>
[Collection("Api")]
public class VolatileRedisPlacementTests(ApiFactory factory)
{
    private const string ChallengeRecordPrefix = "jobbliggaren:auth/challenge/v1/";
    private const string BudgetPrefix = "jobbliggaren:budget/";
    private const string SessionPrefix = "jobbliggaren:session:";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<string>> KeysAsync(string connectionString)
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var server = mux.GetServer(mux.GetEndPoints().Single());
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: "*").WithCancellation(Ct))
            keys.Add(key.ToString());

        return keys;
    }

    private async Task AwaitMailAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!_factory.Emails.LoginChallenges.Any(m => m.ToEmail == email))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }
    }

    [Fact]
    public async Task A_challenge_request_writes_its_record_index_and_budgets_to_the_volatile_instance_and_none_to_the_durable_one()
    {
        var email = $"placement-{Guid.NewGuid():N}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

        (await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        // The mail is sent after the record is written, so its arrival orders this read after the write.
        await AwaitMailAsync(email);

        var volatileKeys = await KeysAsync(_factory.VolatileRedisConnectionString);
        var durableKeys = await KeysAsync(_factory.DurableRedisConnectionString);
        var fingerprint = SubjectFingerprint.Hex(email);

        volatileKeys.ShouldContain(RedisLoginChallengeStore.IndexKey(email));
        volatileKeys.ShouldContain(k => k.StartsWith(ChallengeRecordPrefix, StringComparison.Ordinal));
        volatileKeys.ShouldContain(k =>
            k.StartsWith(BudgetPrefix, StringComparison.Ordinal) && k.EndsWith(fingerprint, StringComparison.Ordinal));

        durableKeys.ShouldNotContain(k => k.Contains("auth/challenge", StringComparison.Ordinal));
        durableKeys.ShouldNotContain(k => k.StartsWith(BudgetPrefix, StringComparison.Ordinal));

        // The control in the other direction: the same scan finds the session this account's creation wrote,
        // on the durable instance and not on the volatile one. A scan that saw nothing would pass the two
        // absences above.
        durableKeys.ShouldContain(k => k.StartsWith(SessionPrefix, StringComparison.Ordinal));
        volatileKeys.ShouldNotContain(k => k.StartsWith(SessionPrefix, StringComparison.Ordinal));
    }
}
