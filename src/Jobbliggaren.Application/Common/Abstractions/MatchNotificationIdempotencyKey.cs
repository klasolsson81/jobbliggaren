using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Resend <c>Idempotency-Key</c> for a background-match notification send (ADR 0080 Vag 4 PR-4
/// item 4, issue #187). Belt-and-suspenders OVER the claim-then-send spine: the spine already stops
/// a Hangfire re-run from re-sending (a re-run finds the row Queued, not Pending), so this key only
/// bites at the TRANSPORT level — an SDK/HttpClient transient retry that re-POSTs within ONE
/// dispatch. Resend dedupes an identical request keyed within 24h; a reused key with a DIFFERENT
/// payload is rejected (409 <c>invalid_idempotent_request</c>), which is exactly why the digest key
/// is content-derived (see <see cref="ForDigest"/>).
/// <para>
/// <b>Deterministic + PII-free by construction (CLAUDE.md §5):</b> built ONLY from opaque
/// <see cref="Guid"/> surrogates (userId, jobAdId / claimed match ids) + the
/// <see cref="DigestCadence"/> enum — never an email address, CV span, or personnummer. Same logical
/// send ⟺ same key ⟺ same payload, so Resend never sees a key/payload mismatch.
/// </para>
/// <para>
/// Two named factories make the illegal combination unconstructable (a Digest without a content
/// fingerprint, a Direct without its ad). Both keys are namespaced + versioned (<c>direct/v1/…</c>,
/// <c>digest/v1/…</c>) so the two kinds can never collide and a future change to the derivation
/// cannot silently reuse a live 24h key. Resend caps the key at 1–256 chars; both forms are well
/// under that. Constructed only via the factories; a <c>default</c> struct carries no key and is
/// rejected fail-loud at the send boundary (<see cref="IEmailSender"/> impl) — never silently
/// downgraded to a non-idempotent send.
/// </para>
/// </summary>
public readonly record struct MatchNotificationIdempotencyKey
{
    // Resend's documented bound is 1..256 chars; our forms are ~75 (Direct) / ~115 (Digest). The
    // guard is a hard contract, not an expected branch.
    private const int MaxLength = 256;
    private const string Version = "v1";

    /// <summary>The wire value handed to the Resend SDK's idempotency overload.</summary>
    public string Value { get; }

    private MatchNotificationIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException(
                $"Idempotency key must be at most {MaxLength} chars (Resend limit).", nameof(value));

        Value = value;
    }

    /// <summary>
    /// Direct (Top) send — one email per Top match, so key the single ad. Entity-based and stable
    /// forever: a re-send of the same Top match for the same user yields the same key (Resend's
    /// recommended <c>&lt;event-type&gt;/&lt;entity-id&gt;</c> shape).
    /// </summary>
    public static MatchNotificationIdempotencyKey ForDirect(Guid userId, Guid jobAdId) =>
        new($"direct/{Version}/{userId:N}/{jobAdId:N}");

    /// <summary>
    /// Digest send — key the CONTENT of the claimed Strong set, not a wall-clock window. The factory
    /// SORTS the ids itself (it never assumes the caller's query ordering) and hashes them, so the
    /// key changes iff the claimed set changes (⟺ the payload changes); two same-period runs that
    /// claimed different sets get different keys (no 409). An empty set is a programmer error — the
    /// digest never sends for an empty window — and is rejected.
    /// </summary>
    public static MatchNotificationIdempotencyKey ForDigest(
        Guid userId, DigestCadence cadence, IEnumerable<Guid> claimedMatchIds)
    {
        ArgumentNullException.ThrowIfNull(claimedMatchIds);

        // Order-independent content fingerprint: sort, render each id in compact 'N' form, '/'-join
        // (the 'N' form is fixed-width hex with no separators, so the join is unambiguous), SHA-256.
        var canonical = string.Join(
            '/', claimedMatchIds.OrderBy(id => id).Select(id => id.ToString("N")));
        if (canonical.Length == 0)
            throw new ArgumentException(
                "Digest idempotency key requires at least one claimed match id.",
                nameof(claimedMatchIds));

        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new($"digest/{Version}/{cadence}/{userId:N}/{hex}");
    }
}
