using Jobbliggaren.Application.Common.Abstractions;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Redis-backed <see cref="IRateBudget"/>, on the volatile instance. The key is
/// <c>jobbliggaren:budget/{scope}/v1/{fingerprint}</c>, the fingerprint being
/// <see cref="SubjectFingerprint.Hex"/>, so two spellings of one account share one counter and the raw
/// address is never written.
/// </summary>
internal sealed class RedisRateBudget(VolatileRedisConnection redis) : IRateBudget
{
    // A raw multiplexer bypasses IDistributedCache's InstanceName, so the namespace is carried by hand
    // (parity RedisSessionStore.KeyPrefix).
    private const string KeyPrefix = "jobbliggaren:";

    public Task<bool> TryConsumeAsync(RateBudgetScope scope, string subject, CancellationToken ct) =>
        redis.ExecuteAsync(async db =>
        {
            var key = Key(scope, subject);

            // INCR and EXPIRE travel as ONE transaction, so a counter never exists without a TTL: a key
            // left without one would lock the address out for good, and with no break-glass nothing would
            // ever lift it (security-auditor, 2026-09-19). NX sets the TTL only on a key that has none, so
            // the window starts at the first counted call and a refused call never extends it.
            var transaction = db.CreateTransaction();
            var count = transaction.StringIncrementAsync(key);
            var expire = transaction.KeyExpireAsync(key, scope.Window, ExpireWhen.HasNoExpiry);
            await transaction.ExecuteAsync();
            await expire;

            return await count <= scope.Limit;
        });

    internal static string Key(RateBudgetScope scope, string subject) =>
        $"{KeyPrefix}budget/{scope.Name}/v1/{SubjectFingerprint.Hex(subject)}";
}
