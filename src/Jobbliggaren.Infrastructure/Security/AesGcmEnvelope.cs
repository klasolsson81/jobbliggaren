using System.Security.Cryptography;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 / ADR 0049 — the single audited AES-256-GCM envelope primitive shared by the string
/// field cipher (<see cref="KmsEnvelopeEncryptor"/>, Form A/B, wraps this as <c>"v1:"+base64</c>)
/// and the binary cipher (<see cref="BinaryFieldEncryptor"/>, Form C, prepends a 1-byte version).
/// Produces/consumes the raw <c>nonce(12) || ciphertext || tag(16)</c> core. Random nonce per
/// <see cref="Seal"/> → ciphertext is never deterministic (equality between PII values does not
/// leak). AEAD: confidentiality + integrity. Fail-closed: auth-tag failure / tampering / wrong DEK
/// → <see cref="CryptographicException"/>, never (partial) plaintext, no PII in the message
/// (CLAUDE.md §5). Stateless → singleton-safe. DRY: one crypto core, no divergent copy.
/// </summary>
internal static class AesGcmEnvelope
{
    public const int NonceSize = 12;      // AES-GCM standard nonce
    public const int TagSize = 16;        // AES-GCM auth tag (128-bit)
    public const int Aes256KeySize = 32;  // DEK = AES-256 (ADR 0049 Beslut 1)

    /// <summary>Seals <paramref name="plaintext"/> under <paramref name="dek"/> into
    /// <c>nonce || ciphertext || tag</c>. Does not zero the caller-owned plaintext.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> dek)
    {
        EnsureAes256Dek(dek);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var payload = new byte[NonceSize + plaintext.Length + TagSize];
        nonce.CopyTo(payload.AsSpan(0));
        var ciphertext = payload.AsSpan(NonceSize, plaintext.Length);
        var tag = payload.AsSpan(NonceSize + plaintext.Length, TagSize);

        using var aes = new AesGcm(dek, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return payload;
    }

    /// <summary>Opens a <c>nonce || ciphertext || tag</c> payload. Throws on a too-short payload,
    /// tampering, or the wrong DEK — never returns (partial) plaintext.</summary>
    public static byte[] Open(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> dek)
    {
        EnsureAes256Dek(dek);

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Envelope-payload är för kort.");

        var nonce = payload[..NonceSize];
        var cipherLength = payload.Length - NonceSize - TagSize;
        var ciphertext = payload.Slice(NonceSize, cipherLength);
        var tag = payload.Slice(NonceSize + cipherLength, TagSize);
        var plaintext = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(dek, TagSize);
            // Throws AuthenticationTagMismatchException (CryptographicException) on wrong DEK /
            // tampering — bubbles, no plaintext returned.
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException(
                "Dekryptering misslyckades (auth-tag-fel eller fel nyckel).");
        }

        return plaintext;
    }

    /// <summary>Enforce AES-256 at the boundary. <see cref="AesGcm"/> also accepts 16/24-byte keys —
    /// a truncated/mis-spec'd DEK would else silently encrypt weaker than the contract (ADR 0049
    /// Beslut 1). No DEK byte in the exception message (§5).</summary>
    public static void EnsureAes256Dek(ReadOnlySpan<byte> dek)
    {
        if (dek.Length != Aes256KeySize)
            throw new CryptographicException($"DEK måste vara {Aes256KeySize} byte (AES-256).");
    }
}
