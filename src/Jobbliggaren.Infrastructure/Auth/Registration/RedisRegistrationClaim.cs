using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.Registration;

/// <summary>
/// The registration claim on the non-persisted Redis (ADR 0142 D1, Amendment 2026-09-19 (2)): one
/// <c>SET … NX</c> carrying its lifetime.
/// </summary>
internal sealed class RedisRegistrationClaim(VolatileRedisConnection redis) : IRegistrationClaim
{
    // A raw multiplexer bypasses IDistributedCache's InstanceName (parity RedisLoginChallengeStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";

    // The key is the whole datum: a fingerprint of the address, so two spellings that reach one account share
    // one claim and the address itself is never written. The value says nothing.
    private const string Claimed = "1";

    public Task<bool> TryClaimAsync(string email, CancellationToken ct) =>
        redis.ExecuteAsync(db => db.StringSetAsync(
            Key(email), Claimed, LoginChallengePolicy.RegistrationClaimTtl, When.NotExists));

    internal static string Key(string email) =>
        $"{KeyPrefix}auth/registration-claim/v1/{SubjectFingerprint.Hex(email)}";
}
