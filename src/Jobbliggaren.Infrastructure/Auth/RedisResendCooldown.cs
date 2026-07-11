using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// #733 — Redis-backed per-target resend cooldown (<see cref="IResendCooldown"/>). Form-precedent
/// <see cref="RedisAccessTokenRevocationStore"/>: a key with an absolute TTL via
/// <see cref="IDistributedCache"/>. The key is a SHA-256 of the NORMALIZED email (trim + lower-invariant,
/// parity with <see cref="AccountExistsNoticeIdempotencyKey.For"/>) — a one-way, non-PII fingerprint (the
/// raw address is never written to Redis); every request on the same address collapses to the same key,
/// so the window is a pure per-address throttle. The window comes from <see cref="ResendCooldownOptions"/>
/// (IOptions-bound; 60s default, security-auditor-ratified).
/// </summary>
internal sealed class RedisResendCooldown(
    IDistributedCache cache,
    IOptions<ResendCooldownOptions> options) : IResendCooldown
{
    private readonly int _windowSeconds = options.Value.WindowSeconds;

    public async Task<bool> TryBeginAsync(string email, CancellationToken ct)
    {
        var key = Key(email);

        // Read-then-write: IDistributedCache exposes no atomic SETNX, but the tiny race (two near-
        // simultaneous first requests both seeing "free") at worst allows one extra send within the
        // window — negligible for an anti-bomb throttle and never a correctness/anti-enum problem (the
        // window is still started existence-independently). A cooled address short-circuits to a silent
        // uniform no-op in the handler.
        if (await cache.GetStringAsync(key, ct) is not null)
            return false;

        await cache.SetStringAsync(
            key,
            "1",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_windowSeconds),
            },
            ct);
        return true;
    }

    // SHA-256 hex of the normalized address (trim + lower-invariant), parity with
    // AccountExistsNoticeIdempotencyKey.For — one-way, non-reversible, never the raw address.
    private static string Key(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"resend-confirm-cd/v1/{hex}";
    }
}
