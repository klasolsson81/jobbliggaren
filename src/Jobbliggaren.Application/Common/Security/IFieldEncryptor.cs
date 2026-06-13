namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// TD-13 (ADR 0049) — symmetrisk fält-kryptering med en data-encryption-key
/// (DEK). Ren krypto-primitiv: ingen AWS-bagage, ingen DEK-livscykel. DEK:en
/// kommer från <see cref="IDataKeyProvider"/> (KMS-envelope). Output bär
/// sentinel-/versionsprefix (<c>v1:</c>) så read-path kan disambiguera
/// krypterat från klartext-legacy och bära DEK-version för key-rotation
/// (ADR 0049 Beslut 4, mekanik-not 2026-05-18).
///
/// Implementeras i Infrastructure (ADR 0009 — krypto är persistens-bekymmer;
/// Domain rörs ej). Fail-closed: integritets-/auth-fel kastar, returnerar
/// aldrig klartext (CTO-domen 2026-05-18).
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>
    /// Krypterar <paramref name="plaintext"/> med <paramref name="dek"/>
    /// (AES-256, 32-byte). Returnerar <c>"v1:" + base64(nonce||ciphertext||tag)</c>.
    /// Slumpmässig nonce per anrop → ciphertext är aldrig deterministisk.
    /// </summary>
    string Encrypt(string plaintext, ReadOnlySpan<byte> dek);

    /// <summary>
    /// Dekrypterar ett sentinel-prefixat värde. Kastar vid auth-tag-fel,
    /// manipulering eller fel DEK — returnerar aldrig (partiell) klartext.
    /// </summary>
    string Decrypt(string sentinelCiphertext, ReadOnlySpan<byte> dek);

    /// <summary>
    /// True om <paramref name="value"/> bär ett sentinel-prefix
    /// (<c>^v\d+:</c>). Klartext-legacy (pre-migrering) saknar prefix.
    /// </summary>
    bool IsEncrypted(string value);
}
