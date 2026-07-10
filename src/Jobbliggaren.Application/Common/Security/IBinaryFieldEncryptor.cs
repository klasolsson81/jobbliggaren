namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5) — the <b>Form C</b> binary sibling of
/// <see cref="IFieldEncryptor"/>: symmetric AES-256-GCM over raw bytes for the
/// <c>resume_files</c> original-file store, under an owner DEK from
/// <see cref="IDataKeyProvider"/>. Byte-in/byte-out (NOT the string <c>"v1:"+base64</c>
/// text envelope — a <c>bytea</c> column stores raw bytes, so base64 would waste ~33%).
///
/// <para>Wire-format: <c>[version(1)] || nonce(12) || ciphertext || tag(16)</c>. A leading
/// 1-byte version tag gives crypto-agility (a future layout fails <i>loud</i> at open, not as
/// an auth-tag mismatch). No field-layer AAD — owner-binding lives entirely on the DEK wrap
/// (<c>LocalDataKeyProvider</c>), so a ciphertext under owner X's DEK is only openable in X's
/// context; adding <c>resume_file_id</c> to AAD would only guard row-swaps within one owner's
/// own data (no meaningful threat) and would fork the audited AES-GCM core.</para>
///
/// <para>Implemented in Infrastructure (ADR 0009 — crypto is a persistence concern; Domain is
/// untouched). Fail-closed: a bad DEK, tampering, or an unknown version → throws, never returns
/// (partial) plaintext, no PII in the exception message (CLAUDE.md §5).</para>
/// </summary>
public interface IBinaryFieldEncryptor
{
    /// <summary>Encrypts <paramref name="plaintext"/> with <paramref name="dek"/> (AES-256, 32-byte)
    /// into <c>[version] || nonce || ciphertext || tag</c>. Random nonce per call.</summary>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> dek);

    /// <summary>Opens a Form C envelope. Throws on a bad version tag, tampering, or the wrong DEK —
    /// never returns (partial) plaintext. (Read-path call-site lands in PR-9b's streaming download.)</summary>
    byte[] Decrypt(ReadOnlySpan<byte> sealedContent, ReadOnlySpan<byte> dek);
}
