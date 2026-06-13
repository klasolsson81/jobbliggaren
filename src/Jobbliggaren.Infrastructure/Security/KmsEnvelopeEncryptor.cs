using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Security;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4) — <see cref="IFieldEncryptor"/> via AES-256-GCM
/// (BCL <see cref="AesGcm"/>, AEAD: konfidentialitet + integritet). Ren
/// symmetrisk primitiv — DEK:en kommer utifrån (<see cref="IDataKeyProvider"/>,
/// KMS-envelope). Ingen AWS-bagage → enhetstestbar utan KMS.
///
/// Wire-format: <c>"v1:" + base64(nonce(12) || ciphertext || tag(16))</c>.
/// Slumpmässig nonce per <see cref="Encrypt"/> → ciphertext är aldrig
/// deterministisk (likhet mellan PII-fält läcker inte). Fail-closed:
/// auth-tag-fel/manipulering/fel DEK → <see cref="CryptographicException"/>,
/// aldrig (partiell) klartext, ingen PII i exception-message (CLAUDE.md §5.4,
/// CTO-domen 2026-05-18). Stateless → singleton-säker.
/// </summary>
public sealed partial class KmsEnvelopeEncryptor : IFieldEncryptor
{
    private const int NonceSize = 12;  // AES-GCM standard-nonce
    private const int TagSize = 16;    // AES-GCM auth-tag (128-bit)
    private const int Aes256KeySize = 32;  // DEK = AES-256 (ADR 0049 Beslut 1)

    [GeneratedRegex(@"^v\d+:", RegexOptions.CultureInvariant)]
    private static partial Regex SentinelPattern();

    public string Encrypt(string plaintext, ReadOnlySpan<byte> dek)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        EnsureAes256Dek(dek);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(dek, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        // nonce || ciphertext || tag
        var payload = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(payload.AsSpan(0));
        ciphertext.CopyTo(payload.AsSpan(NonceSize));
        tag.CopyTo(payload.AsSpan(NonceSize + ciphertext.Length));

        CryptographicOperations.ZeroMemory(plaintextBytes);

        return FieldEncryptionSentinel.VersionPrefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string sentinelCiphertext, ReadOnlySpan<byte> dek)
    {
        ArgumentNullException.ThrowIfNull(sentinelCiphertext);
        EnsureAes256Dek(dek);

        var colon = sentinelCiphertext.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || !IsEncrypted(sentinelCiphertext))
        {
            // Ingen klartext-fallback — okänt format är fail-closed.
            throw new CryptographicException(
                "Värdet saknar giltigt sentinel-prefix och kan inte dekrypteras.");
        }

        // Explicit versions-guard (Minor 3, ADR 0049 Beslut 4 crypto-agility):
        // endast v1-layouten (nonce12||ct||tag16) är känd. En framtida v2 med
        // annan layout ska fela tydligt här, inte som auth-tag-mismatch.
        if (!sentinelCiphertext.StartsWith(
                FieldEncryptionSentinel.VersionPrefix, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Okänd sentinel-version — endast v1 stöds av denna decryptor.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(sentinelCiphertext[(colon + 1)..]);
        }
        catch (FormatException)
        {
            throw new CryptographicException("Sentinel-payload är inte giltig base64.");
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Sentinel-payload är för kort.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var cipherLength = payload.Length - NonceSize - TagSize;
        var ciphertext = payload.AsSpan(NonceSize, cipherLength);
        var tag = payload.AsSpan(NonceSize + cipherLength, TagSize);
        var plaintextBytes = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(dek, TagSize);
            // Kastar AuthenticationTagMismatchException (CryptographicException)
            // vid fel DEK / manipulering — bubblar, ingen klartext returneras.
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }
        catch (CryptographicException)
        {
            // Rensa ev. partiell buffert; kasta utan PII i message.
            CryptographicOperations.ZeroMemory(plaintextBytes);
            throw new CryptographicException(
                "Dekryptering misslyckades (auth-tag-fel eller fel nyckel).");
        }

        var result = Encoding.UTF8.GetString(plaintextBytes);
        CryptographicOperations.ZeroMemory(plaintextBytes);
        return result;
    }

    public bool IsEncrypted(string value) =>
        !string.IsNullOrEmpty(value) && SentinelPattern().IsMatch(value);

    // Major 1 (security-auditor 2026-05-18): enforca AES-256 vid gränsen.
    // AesGcm accepterar även 16/24-byte-nycklar — en trunkerad/fel-spec:ad
    // DEK skulle annars tyst kryptera svagare än kontraktet (ADR 0049
    // Beslut 1). Ingen DEK-byte i exception-message (§5.4).
    private static void EnsureAes256Dek(ReadOnlySpan<byte> dek)
    {
        if (dek.Length != Aes256KeySize)
        {
            throw new CryptographicException(
                $"DEK måste vara {Aes256KeySize} byte (AES-256).");
        }
    }
}
