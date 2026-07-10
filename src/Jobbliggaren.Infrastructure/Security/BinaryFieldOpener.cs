using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Fas 4b PR-9b (ADR 0100 §D3 read-path, M-F2) — <see cref="IBinaryFieldOpener"/> over the
/// scope-bound DEK cache, the exact read-side mirror of <see cref="BinaryFieldSealer"/>. Peeks the
/// owner DEK <c>FieldEncryptionKeyPrefetchBehavior</c> warmed
/// (<see cref="ScopedUserDataKeyCache.TryPeekCachedDek"/> — sync by design; §3.5 forbids
/// sync-over-async), with the same deliberate posture as the sealer: opening is NEVER
/// scope-differentiated. There is no legitimate system-scope read of an owner's original (only the
/// authenticated owner downloads their own file), so a missing owner or a cold cache ALWAYS throws
/// — fail-closed, a stranger's plaintext is never returned and a partial/unverified buffer never
/// leaves this method. The peeked buffer is cache-owned (zeroed at scope dispose): it is read as a
/// span for the decrypt call only — never copied, mutated, or zeroed here (§5). The returned
/// plaintext is owned by the caller (the download DTO), never by the aggregate.
/// </summary>
public sealed class BinaryFieldOpener(
    ScopedUserDataKeyCache cache,
    ICurrentDataOwner currentDataOwner,
    IBinaryFieldEncryptor encryptor) : IBinaryFieldOpener
{
    public byte[] Open(ReadOnlyMemory<byte> sealedContent)
    {
        var owner = currentDataOwner.JobSeekerId
            ?? throw new CryptographicException(
                "BinaryFieldOpener: ingen ägare i scopet — frågan måste bära "
                + "IRequiresFieldEncryptionKey så FieldEncryptionKeyPrefetchBehavior "
                + "sätter ägare + värmer ägar-DEK (ADR 0049 Mekanik-not 3/4).");

        if (!cache.TryPeekCachedDek(owner, out var dek))
        {
            throw new CryptographicException(
                "BinaryFieldOpener: ingen cachad DEK för ägaren — "
                + "FieldEncryptionKeyPrefetchBehavior måste ha värmt ägar-DEK "
                + "(ADR 0049 Mekanik-not 3/4).");
        }

        // Cache-owned DEK buffer read as a span for the decrypt only — never copied/mutated/zeroed
        // here. Decrypt verifies the AES-GCM tag over the whole envelope and fails loud (throwing,
        // zeroing its output) on a version-byte mismatch, tampering, or a wrong DEK.
        return encryptor.Decrypt(sealedContent.Span, dek);
    }
}
