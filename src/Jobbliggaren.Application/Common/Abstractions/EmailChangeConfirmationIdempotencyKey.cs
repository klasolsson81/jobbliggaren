using System.Security.Cryptography;
using System.Text;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Resend <c>Idempotency-Key</c> for the change-email confirmation send (#679). Parity with
/// <see cref="MatchNotificationIdempotencyKey"/>: deterministic + PII-free by construction — built
/// from the opaque userId surrogate + a SHA-256 of the (already opaque, non-PII) URL-safe token,
/// never the email address or personnummer. Two distinct change requests carry two tokens → two
/// keys → both delivered; a transport-level retry of the SAME send re-uses the key → Resend dedupes.
/// Namespaced + versioned so it can never collide with the notice key or a future change. Constructed
/// only via <see cref="For"/>; a <c>default</c> struct is rejected fail-loud at the send boundary
/// (the <see cref="IEmailSender"/> impl).
/// </summary>
public readonly record struct EmailChangeConfirmationIdempotencyKey
{
    private const int MaxLength = 256;
    private const string Version = "v1";

    /// <summary>The wire value handed to the Resend SDK's idempotency overload.</summary>
    public string Value { get; }

    private EmailChangeConfirmationIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key must be non-empty.", nameof(value));
        if (value.Length > MaxLength)
            throw new ArgumentException(
                $"Idempotency key must be at most {MaxLength} chars (Resend limit).", nameof(value));

        Value = value;
    }

    /// <summary>
    /// Key the send by (user, token): the token uniquely identifies this change-email flow and is
    /// opaque/non-PII, so its SHA-256 is a safe, stable fingerprint. Hashed (not raw) to stay well
    /// under Resend's 256-char cap regardless of token length.
    /// </summary>
    public static EmailChangeConfirmationIdempotencyKey For(Guid userId, string urlSafeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlSafeToken);
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(urlSafeToken)));
        return new($"change-email-confirm/{Version}/{userId:N}/{hex}");
    }
}
