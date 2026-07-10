using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5) — <b>Form C</b> binary field cipher
/// (<see cref="IBinaryFieldEncryptor"/>) over AES-256-GCM via the shared
/// <see cref="AesGcmEnvelope"/> core. Wire-format for the <c>resume_files.content</c>
/// <c>bytea</c>: <c>[version(1)] || nonce(12) || ciphertext || tag(16)</c> — raw bytes, no
/// base64 (that is the string cipher's text-column concern). The 1-byte version tag mirrors
/// <c>LocalDataKeyProvider</c>'s <c>[magic, version]</c> guard: a future v2 layout fails
/// <i>loud</i> at open, not as an auth-tag mismatch. No field-layer AAD (owner-binding lives on
/// the DEK wrap). Stateless → singleton-safe. Fail-closed (CLAUDE.md §5).
/// </summary>
public sealed class BinaryFieldEncryptor : IBinaryFieldEncryptor
{
    /// <summary>Form C envelope version (crypto-agility). v1 = the layout above.</summary>
    private const byte FormCVersion = 0x01;

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> dek)
    {
        AesGcmEnvelope.EnsureAes256Dek(dek);

        var core = AesGcmEnvelope.Seal(plaintext, dek); // nonce || ct || tag
        var envelope = new byte[1 + core.Length];
        envelope[0] = FormCVersion;
        core.CopyTo(envelope.AsSpan(1));
        return envelope;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> sealedContent, ReadOnlySpan<byte> dek)
    {
        AesGcmEnvelope.EnsureAes256Dek(dek);

        if (sealedContent.Length < 1)
            throw new CryptographicException("Form C-envelope saknar version-byte.");

        // Explicit version guard (crypto-agility): only v1 layout (nonce12||ct||tag16) is known.
        // A future v2 must fail clearly here, not as an auth-tag mismatch.
        if (sealedContent[0] != FormCVersion)
            throw new CryptographicException(
                "Okänd Form C-version — endast v1 stöds av denna decryptor.");

        return AesGcmEnvelope.Open(sealedContent[1..], dek);
    }
}
