namespace Jobbliggaren.Application.Common.Security;

/// <summary>
/// Fas 4b PR-9b (ADR 0100 §D3 read-path, M-F2) — the owner-aware READ-path opener for the
/// <c>resume_files</c> Form C store, symmetric to <see cref="IBinaryFieldSealer"/>. The download
/// endpoint decrypts a stored original's opaque Form C envelope back to plaintext bytes for the
/// owner; this is the port that does it, keyed off the same scope-bound DEK cache the sealer uses.
///
/// <para>The implementation (Infrastructure) resolves the owner DEK from the scoped cache the
/// <c>FieldEncryptionKeyPrefetchBehavior</c> already warmed — so any query whose handler calls this
/// MUST carry <see cref="IRequiresFieldEncryptionKey"/> (pinned by an architecture test, symmetric
/// to the sealer pin). Fail-closed: if there is no owner in scope or the owner DEK is not warm,
/// <see cref="Open"/> throws (never returns an empty or partial buffer). Unlike the string-field
/// materialization interceptor, there is NO system-scope arm — only an authenticated owner ever
/// reads their own original, so a missing owner or cold cache ALWAYS throws.</para>
/// </summary>
public interface IBinaryFieldOpener
{
    /// <summary>Opens a Form C envelope (<see cref="IBinaryFieldEncryptor"/>) under the current
    /// owner's warmed DEK, returning the plaintext bytes. Throws if the DEK is not warmed
    /// (fail-closed) — the caller must be an <see cref="IRequiresFieldEncryptionKey"/> query. The
    /// AES-GCM tag is verified in full before any plaintext is returned, so the buffer is never
    /// partial or unverified.</summary>
    byte[] Open(ReadOnlyMemory<byte> sealedContent);
}
