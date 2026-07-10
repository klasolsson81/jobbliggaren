namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5, CTO Q2 = explicit seal) — owner-aware write-path seal for the
/// <c>resume_files</c> Form C store. Unlike the string Form A/B pipeline (which encrypts
/// transparently in a SaveChanges interceptor keyed off <c>EncryptedFieldRegistry</c>), a
/// binary original is sealed EXPLICITLY here and the <c>ResumeFile</c> aggregate holds the
/// resulting opaque ciphertext (sealed-at-construction). Rationale: Form C's real read path is
/// streaming (PR-9b), which never engages the materialization interceptor — so an interceptor
/// arm would be dead code and would force the aggregate to expose change-tracked multi-MB
/// plaintext (§5 minimisation violation).
///
/// <para>The implementation (Infrastructure) resolves the owner DEK from the scoped cache the
/// <c>FieldEncryptionKeyPrefetchBehavior</c> already warmed — so any command whose handler calls
/// this MUST carry <see cref="IRequiresFieldEncryptionKey"/> (pinned by an architecture test).
/// Fail-closed: if the owner DEK is not warm, <see cref="Seal"/> throws (never a silent no-op),
/// so no plaintext is ever persisted unsealed.</para>
/// </summary>
public interface IBinaryFieldSealer
{
    /// <summary>Seals <paramref name="plaintext"/> under the current owner's warmed DEK into a
    /// Form C envelope (<see cref="IBinaryFieldEncryptor"/>). Throws if the DEK is not warmed
    /// (fail-closed) — the caller must be an <see cref="IRequiresFieldEncryptionKey"/> command.</summary>
    byte[] Seal(ReadOnlyMemory<byte> plaintext);
}
