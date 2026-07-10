using System.Security.Cryptography;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// Fas 4b PR-9a (ADR 0100, CTO Q2 = explicit seal) — verifies <see cref="BinaryFieldSealer"/>:
/// seals under the owner DEK the scoped cache holds (peek — same warmed cache the prefetch
/// behavior fills), and is UNCONDITIONALLY fail-closed: no owner in scope or a cold cache
/// throws, never a silent no-op and never unsealed plaintext. Uses the REAL cache + cipher
/// (in-assembly collaborators; only the DEK unwrap is a test factory).
/// </summary>
public class BinaryFieldSealerTests
{
    private static byte[] Dek()
    {
        var dek = new byte[32];
        for (var i = 0; i < dek.Length; i++)
        {
            dek[i] = (byte)(i * 7 + 3);
        }

        return dek;
    }

    private static readonly byte[] Plaintext = [0x25, 0x50, 0x44, 0x46, 0x01, 0x02, 0x03];

    [Fact]
    public async Task Seal_WithWarmedOwnerDek_ProducesEnvelopeDecryptableUnderSameDek()
    {
        var owner = new JobSeekerId(Guid.NewGuid());
        using var cache = new ScopedUserDataKeyCache();
        // Warm the cache the same way FieldEncryptionKeyPrefetchBehavior does (via the
        // unwrap factory); the sealer must then peek THIS DEK.
        await cache.GetOrUnwrapAsync(owner, () => Task.FromResult(Dek()), CancellationToken.None);
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(owner);
        var encryptor = new BinaryFieldEncryptor();
        var sut = new BinaryFieldSealer(cache, currentOwner, encryptor);

        var sealedContent = sut.Seal(Plaintext);

        sealedContent.ShouldNotBe(Plaintext); // opaque envelope, never pass-through
        encryptor.Decrypt(sealedContent, Dek()).ShouldBe(Plaintext);
    }

    [Fact]
    public void Seal_WithoutOwnerInScope_ThrowsFailClosed()
    {
        using var cache = new ScopedUserDataKeyCache();
        var sut = new BinaryFieldSealer(cache, new CurrentDataOwner(), new BinaryFieldEncryptor());

        var ex = Should.Throw<CryptographicException>(() => sut.Seal(Plaintext));

        ex.Message.ShouldContain("IRequiresFieldEncryptionKey");
    }

    [Fact]
    public void Seal_WithOwnerButColdCache_ThrowsFailClosed()
    {
        // Owner resolved but the prefetch never warmed the DEK (e.g. the command forgot the
        // marker interface): sealing MUST throw — plaintext is never persisted unsealed.
        using var cache = new ScopedUserDataKeyCache();
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(new JobSeekerId(Guid.NewGuid()));
        var sut = new BinaryFieldSealer(cache, currentOwner, new BinaryFieldEncryptor());

        var ex = Should.Throw<CryptographicException>(() => sut.Seal(Plaintext));

        ex.Message.ShouldContain("FieldEncryptionKeyPrefetchBehavior");
    }

    [Fact]
    public async Task Seal_WithOtherOwnersDekWarmed_ThrowsFailClosed()
    {
        // Cross-owner isolation: a DEK warmed for ANOTHER owner never seals this owner's
        // bytes — the peek is keyed by the CURRENT owner, not "any warm DEK".
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(new JobSeekerId(Guid.NewGuid()));
        using var cache = new ScopedUserDataKeyCache();
        await cache.GetOrUnwrapAsync(
            new JobSeekerId(Guid.NewGuid()), // a DIFFERENT owner's DEK
            () => Task.FromResult(Dek()),
            CancellationToken.None);
        var sut = new BinaryFieldSealer(cache, currentOwner, new BinaryFieldEncryptor());

        Should.Throw<CryptographicException>(() => sut.Seal(Plaintext));
    }
}
