using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5 / ADR 0100, CTO Q2 = explicit seal) — <see cref="IBinaryFieldSealer"/>
/// over the scope-bound DEK cache. Mirrors <see cref="FieldDecryptionMaterializationInterceptor"/>'s
/// synchronous in-assembly peek of the cache <c>FieldEncryptionKeyPrefetchBehavior</c> warmed
/// (<see cref="ScopedUserDataKeyCache.TryPeekCachedDek"/> — sync by design; §3.5 forbids
/// sync-over-async), with ONE deliberate difference: sealing is NEVER scope-differentiated.
/// There is no legitimate system-scope seal (only an authenticated owner imports a CV), so a
/// missing owner or a cold cache ALWAYS throws — fail-closed, plaintext is never persisted
/// unsealed and never silently dropped. The peeked buffer is cache-owned (zeroed at scope
/// dispose): it is read as a span for the encrypt call only — never copied, mutated, or zeroed
/// here, so no extra plaintext-DEK copy exists to leak (§5).
/// </summary>
public sealed class BinaryFieldSealer(
    ScopedUserDataKeyCache cache,
    ICurrentDataOwner currentDataOwner,
    IBinaryFieldEncryptor encryptor) : IBinaryFieldSealer
{
    public byte[] Seal(ReadOnlyMemory<byte> plaintext)
    {
        var owner = currentDataOwner.JobSeekerId
            ?? throw new CryptographicException(
                "BinaryFieldSealer: ingen ägare i scopet — kommandot måste bära "
                + "IRequiresFieldEncryptionKey så FieldEncryptionKeyPrefetchBehavior "
                + "sätter ägare + värmer ägar-DEK (ADR 0049 Mekanik-not 3/4).");

        if (!cache.TryPeekCachedDek(owner, out var dek))
        {
            throw new CryptographicException(
                "BinaryFieldSealer: ingen cachad DEK för ägaren — "
                + "FieldEncryptionKeyPrefetchBehavior måste ha värmt ägar-DEK "
                + "(ADR 0049 Mekanik-not 3/4).");
        }

        return encryptor.Encrypt(plaintext.Span, dek);
    }
}
