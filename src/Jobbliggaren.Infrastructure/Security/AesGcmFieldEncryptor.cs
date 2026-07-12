using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Security;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4) — <see cref="IFieldEncryptor"/> via AES-256-GCM
/// (BCL <see cref="AesGcm"/>, AEAD: konfidentialitet + integritet). Ren
/// symmetrisk primitiv — DEK:en kommer utifrån
/// (<see cref="IDataKeyProvider"/>, lokal envelope, ADR 0066). AWS-fri och
/// oberoende av DEK-wrap-mekanismen (namnet var tidigare KmsEnvelopeEncryptor
/// men klassen har aldrig rört AWS — den delas oförändrad av Local-providern).
///
/// Wire-format: <c>"v1:" + base64(nonce(12) || ciphertext || tag(16))</c>.
/// AES-GCM-kärnan (nonce/seal/open/fail-closed) delas med Form C via
/// <see cref="AesGcmEnvelope"/> (Fas 4b PR-9a / ADR 0100 — EN audit-yta, ingen
/// divergent kopia); den här klassen äger bara STRÄNG-skalet: UTF-8,
/// sentinel-prefix, base64 och klartextbuffert-nollning. Slumpmässig nonce per
/// <see cref="Encrypt"/> → ciphertext är aldrig deterministisk (likhet mellan
/// PII-fält läcker inte). Fail-closed: auth-tag-fel/manipulering/fel DEK →
/// <see cref="CryptographicException"/>, aldrig (partiell) klartext, ingen PII
/// i exception-message (CLAUDE.md §5.4, CTO-domen 2026-05-18). Wire-formatet är
/// pinnat av ett fryst pre-refaktor-ciphertext-test (back-compat: befintlig
/// at-rest-data måste alltid förbli dekrypterbar). Stateless → singleton-säker.
/// </summary>
public sealed partial class AesGcmFieldEncryptor : IFieldEncryptor
{
    [GeneratedRegex(@"^v\d+:", RegexOptions.CultureInvariant)]
    private static partial Regex SentinelPattern();

    public string Encrypt(string plaintext, ReadOnlySpan<byte> dek)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        AesGcmEnvelope.EnsureAes256Dek(dek);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var payload = AesGcmEnvelope.Seal(plaintextBytes, dek);
        CryptographicOperations.ZeroMemory(plaintextBytes);

        // Emitterar alltid v1-sentinel (single-version-invariant, #501). Prefixet
        // och UserDataKeyStore.CurrentDekVersion är låsta till samma version — en
        // framtida bump måste flytta BÅDA + göra Decrypt/ResolveDek versionsmedvetna
        // + köra en re-encrypt-migration, annars bricks befintlig ciphertext.
        return FieldEncryptionSentinel.VersionPrefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string sentinelCiphertext, ReadOnlySpan<byte> dek)
    {
        ArgumentNullException.ThrowIfNull(sentinelCiphertext);
        AesGcmEnvelope.EnsureAes256Dek(dek);

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

        // Sträng-lagrets egen längd-guard FÖRE kärnan så det etablerade
        // "Sentinel-payload är för kort."-meddelandet bevaras (refaktorn är
        // beteende-bevarande; kärnans generiska envelope-meddelande nås aldrig).
        if (payload.Length < AesGcmEnvelope.NonceSize + AesGcmEnvelope.TagSize)
        {
            throw new CryptographicException("Sentinel-payload är för kort.");
        }

        // Kärnan är fail-closed: auth-tag-fel/manipulering/fel DEK kastar
        // CryptographicException utan (partiell) klartext och nollar sin buffert.
        var plaintextBytes = AesGcmEnvelope.Open(payload, dek);

        var result = Encoding.UTF8.GetString(plaintextBytes);
        CryptographicOperations.ZeroMemory(plaintextBytes);
        return result;
    }

    public bool IsEncrypted(string value) =>
        !string.IsNullOrEmpty(value) && SentinelPattern().IsMatch(value);
}
