using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Redis-backed <see cref="ICooldownGate"/> (generalised from the #733 resend primitive; #703).
/// Form: a key with an absolute TTL via <see cref="IDistributedCache"/>. The key is
/// <c>cd/{scope}/v1/{sha256(subject)}</c> — the subject (an
/// email address or a user id) is normalised the way Identity normalises a lookup key
/// (<c>Trim()</c> + <c>Normalize()</c> + <c>ToUpperInvariant()</c>, see <see cref="SubjectFingerprint"/>
/// for why) and SHA-256-hashed, a one-way fingerprint
/// (the raw value is never written to Redis); every call on the same <c>(scope, subject)</c> collapses to
/// the same key, so the window is a pure per-subject throttle. Pure mechanism: the window is a caller
/// parameter (OCP — a new scope adds a caller, never edits this class), and the policy (window length,
/// silent-vs-visible on a cooled call) lives in the calling handler.
/// </summary>
internal sealed class RedisCooldownGate(IDistributedCache cache) : ICooldownGate
{
    public async Task<bool> TryBeginAsync(string scope, string subject, TimeSpan window, CancellationToken ct)
    {
        var key = Key(scope, subject);

        // Read-then-write: IDistributedCache exposes no atomic SETNX, but the tiny race (two near-
        // simultaneous first requests both seeing "free") at worst allows one extra send within the
        // window — negligible for an anti-bomb throttle and never a correctness / anti-enum problem (the
        // window is still started existence-independently). A cooled subject short-circuits in the handler.
        if (await cache.GetStringAsync(key, ct) is not null)
            return false;

        await cache.SetStringAsync(
            key,
            "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window },
            ct);
        return true;
    }

    // The subject's normalisation and hash have one home, shared with every other Redis key derived from
    // an address.
    private static string Key(string scope, string subject) =>
        $"cd/{scope}/v1/{SubjectFingerprint.Hex(subject)}";
}
