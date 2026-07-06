using System.Security.Cryptography;
using System.Text;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Resend <c>Idempotency-Key</c> for the old-address "your email was changed" security notice (#679,
/// CTO-bind #4). Parity with <see cref="EmailChangeConfirmationIdempotencyKey"/>: deterministic +
/// PII-free (userId surrogate + SHA-256 of the opaque token, never an email address or personnummer).
/// A distinct <c>change-email-notice/</c> namespace so it can never collide with the confirmation
/// key. Constructed only via <see cref="For"/>; a <c>default</c> struct is rejected fail-loud at the
/// send boundary.
/// </summary>
public readonly record struct EmailChangedNotificationIdempotencyKey
{
    private const int MaxLength = 256;
    private const string Version = "v1";

    /// <summary>The wire value handed to the Resend SDK's idempotency overload.</summary>
    public string Value { get; }

    private EmailChangedNotificationIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException(
                $"Idempotency key must be at most {MaxLength} chars (Resend limit).", nameof(value));

        Value = value;
    }

    /// <summary>
    /// Key the notice by (user, token): the same change-email flow's token is a stable, opaque,
    /// non-PII surrogate. SHA-256-hashed to stay under Resend's 256-char cap.
    /// </summary>
    public static EmailChangedNotificationIdempotencyKey For(Guid userId, string urlSafeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlSafeToken);
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(urlSafeToken)));
        return new($"change-email-notice/{Version}/{userId:N}/{hex}");
    }
}
