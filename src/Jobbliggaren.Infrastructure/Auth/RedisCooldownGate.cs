using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Redis-backed <see cref="ICooldownGate"/> (generalised from the #733 resend primitive; #703).
/// Form: a key with an absolute TTL via <see cref="IDistributedCache"/>. The key is
/// <c>cd/{scope}/v1/{sha256(subject)}</c> — the subject (an
/// email address or a user id) is normalised (<c>Trim()</c> + <c>ToLowerInvariant()</c>) and
/// SHA-256-hashed, a one-way non-PII fingerprint
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

    // SHA-256 hex of the normalized subject — one-way, non-reversible, never the raw value.
    //
    // The normalisation MUST be Identity's, not merely "a" normalisation (#1171, security-auditor
    // 2026-08-10). Every subject here is an email address, and the throttle is only meaningful if two
    // spellings that reach the SAME ACCOUNT land on the SAME KEY. Identity's lookup is
    // UpperInvariantLookupNormalizer — `Normalize().ToUpperInvariant()`, i.e. NFC then upper — and the
    // previous `ToLowerInvariant()` was not its inverse.
    //
    // Two BMP characters break the old form, and both are trivially typeable: U+017F LATIN SMALL LETTER
    // LONG S (ſ) upper-cases to 'S', and U+0131 LATIN SMALL LETTER DOTLESS I (ı) upper-cases to 'I' —
    // while both lower-case to themselves. So `klaſ.olſſon@…` and `admın@…` pass the validator, resolve
    // to the same Identity account as their ASCII spellings, and used to get their OWN 60 s window: 2^k
    // independent windows for an address with k such letters. NFC closes the second axis, where a
    // decomposed (NFD) spelling of any accented address did the same.
    //
    // This is a shared gate, so the fix covers all four scopes (resend-confirm, account-exists,
    // change-email-target/user, password-reset), not only the one that surfaced it. Changing the derived
    // key resets in-flight windows exactly once, which is harmless at a 60 s window. `ToUpperInvariant`
    // rather than `ToLower` deliberately: it is what Identity does, and the Turkish-I hazard runs the
    // other way (`ToLower` on the invariant culture is what people reach for and what was wrong here).
    private static string Key(string scope, string subject)
    {
        var normalized = subject.Trim().Normalize().ToUpperInvariant();
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"cd/{scope}/v1/{hex}";
    }
}
