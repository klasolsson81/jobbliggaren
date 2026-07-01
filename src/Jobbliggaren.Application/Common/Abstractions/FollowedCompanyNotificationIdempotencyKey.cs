using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Resend <c>Idempotency-Key</c> for a company-follow notification send (ADR 0087 D5, #311 PR-4).
/// A SEPARATE, namespaced (<c>follow/v1/…</c>) key type from
/// <see cref="MatchNotificationIdempotencyKey"/> (senior-cto-advisor D1, 2026-07-01) so the two
/// dispatch payloads can NEVER collide on a shared Resend key (a reused key with a different payload
/// is a 409 <c>invalid_idempotent_request</c> at Resend).
/// <para>
/// Belt-and-suspenders OVER the claim-then-send spine (a Hangfire re-run finds the hit Queued, not
/// Pending, so it never re-dispatches); this key only bites at the TRANSPORT level — an
/// SDK/HttpClient transient retry that re-POSTs within ONE dispatch. Resend dedupes an identical
/// request keyed within 24h.
/// </para>
/// <para>
/// <b>Deterministic + PII-free by construction (CLAUDE.md §5):</b> built ONLY from opaque
/// <see cref="Guid"/> surrogates (userId + the claimed hit ids) + the <see cref="DigestCadence"/>
/// enum — never an email address, org.nr, or personnummer. The factory SORTS the ids itself and
/// hashes them, so the key changes iff the claimed set changes (⟺ the payload changes); two
/// same-period runs that claimed different sets get different keys (no 409). Constructed only via
/// <see cref="ForDigest"/>; a <c>default</c> struct carries no key and is rejected fail-loud at the
/// send boundary (never silently downgraded to a non-idempotent send).
/// </para>
/// </summary>
public readonly record struct FollowedCompanyNotificationIdempotencyKey
{
    private const int MaxLength = 256;
    private const string Version = "v1";

    /// <summary>The wire value handed to the Resend SDK's idempotency overload.</summary>
    public string Value { get; }

    private FollowedCompanyNotificationIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException(
                $"Idempotency key must be at most {MaxLength} chars (Resend limit).", nameof(value));

        Value = value;
    }

    /// <summary>
    /// Company-follow digest send — key the CONTENT of the claimed hit set, not a wall-clock window.
    /// The factory SORTS the ids itself (never assumes the caller's query ordering) and hashes them,
    /// so the key changes iff the claimed set changes. An empty set is a programmer error — the
    /// digest never sends for an empty window — and is rejected.
    /// </summary>
    public static FollowedCompanyNotificationIdempotencyKey ForDigest(
        Guid userId, DigestCadence cadence, IEnumerable<Guid> claimedHitIds)
    {
        ArgumentNullException.ThrowIfNull(claimedHitIds);

        // Order-independent content fingerprint: sort, render each id in compact 'N' form, '/'-join,
        // SHA-256 (parity MatchNotificationIdempotencyKey.ForDigest).
        var canonical = string.Join(
            '/', claimedHitIds.OrderBy(id => id).Select(id => id.ToString("N")));
        if (canonical.Length == 0)
            throw new ArgumentException(
                "Follow-notification idempotency key requires at least one claimed hit id.",
                nameof(claimedHitIds));

        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new($"follow/{Version}/{cadence}/{userId:N}/{hex}");
    }
}
