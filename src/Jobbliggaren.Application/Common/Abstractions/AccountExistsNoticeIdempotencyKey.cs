using System.Security.Cryptography;
using System.Text;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Resend <c>Idempotency-Key</c> for the registration account-exists notice (#714), sent out-of-band
/// to a TAKEN address when someone attempts to register it (anti-enumeration: the HTTP response is the
/// same 202 as a fresh signup, the only differentiator is this out-of-band mail).
/// <para>
/// No token exists on this path and the duplicate-swallow branch deliberately does NOT look up the
/// existing account, so there is no userId/token surrogate available. The key is therefore a SHA-256
/// of the NORMALIZED recipient address — a one-way, non-reversible fingerprint (never the raw address;
/// parity with the login audit's email hashing). This also makes every registration attempt on the
/// same taken address collapse to the SAME key, so Resend delivers at most one notice per address per
/// dedup window — a natural anti-email-bomb property (CTO-bind Risk 6). Distinct
/// <c>account-exists-notice/</c> namespace. Constructed only via <see cref="For"/>; a <c>default</c>
/// struct is rejected fail-loud at the send boundary.
/// </para>
/// </summary>
public readonly record struct AccountExistsNoticeIdempotencyKey
{
    private const int MaxLength = 256;
    private const string Version = "v1";

    /// <summary>The wire value handed to the Resend SDK's idempotency overload.</summary>
    public string Value { get; }

    private AccountExistsNoticeIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException(
                $"Idempotency key must be at most {MaxLength} chars (Resend limit).", nameof(value));

        Value = value;
    }

    /// <summary>
    /// Key the notice by the normalized recipient address (trim + lower-invariant, parity with the
    /// login audit's <c>HashEmail</c>): no token/userId is available here, and the SHA-256 is a
    /// one-way, non-PII surrogate that also dedupes repeated attempts on the same address.
    /// </summary>
    public static AccountExistsNoticeIdempotencyKey For(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new($"account-exists-notice/{Version}/{hex}");
    }
}
