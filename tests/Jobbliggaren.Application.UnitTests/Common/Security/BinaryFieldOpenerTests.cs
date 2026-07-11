using System.Security.Cryptography;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// Fas 4b PR-9b (ADR 0100 §D3 read-path, M-F2) — verifies <see cref="BinaryFieldOpener"/>, the
/// read-side mirror of <see cref="BinaryFieldSealer"/>: it peeks the owner DEK the scoped cache
/// holds (the same warmed cache the prefetch behavior fills) and opens a Form C envelope back to
/// the exact plaintext bytes, and is UNCONDITIONALLY fail-closed — no owner in scope or a cold
/// cache throws (never a stranger's plaintext, never a partial/unverified buffer). Uses the REAL
/// cache + cipher (in-assembly collaborators; only the DEK unwrap is a test factory), mirroring
/// <see cref="BinaryFieldSealerTests"/> exactly.
/// </summary>
public class BinaryFieldOpenerTests
{
    // Same fixed 32-byte DEK recipe as BinaryFieldSealerTests/BinaryFieldEncryptorTests —
    // deterministic in test; the real DEK comes from IDataKeyProvider in production.
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
    public async Task Open_WithWarmedOwnerDek_ReturnsExactPlaintextSealedBySealer()
    {
        // Full symmetric round-trip: seal under the owner's warmed DEK, then open under the SAME
        // warmed cache — the opener must recover the exact plaintext the sealer sealed.
        var owner = new JobSeekerId(Guid.NewGuid());
        using var cache = new ScopedUserDataKeyCache();
        await cache.GetOrUnwrapAsync(owner, () => Task.FromResult(Dek()), CancellationToken.None);
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(owner);
        var encryptor = new BinaryFieldEncryptor();
        var sealer = new BinaryFieldSealer(cache, currentOwner, encryptor);
        var sut = new BinaryFieldOpener(cache, currentOwner, encryptor);

        var sealedContent = sealer.Seal(Plaintext);
        var opened = sut.Open(sealedContent);

        opened.ShouldBe(Plaintext);
        opened.ShouldNotBeSameAs(Plaintext); // a fresh decrypt buffer, never the input reference
    }

    [Fact]
    public async Task Open_WithWarmedOwnerDek_ReturnsExactPlaintextEncryptedByCipher()
    {
        // The read-side of the sealer test's `encryptor.Decrypt(sealed, Dek())` assertion: an
        // envelope produced by the raw cipher under Dek() opens via the opener that peeks the
        // owner DEK warmed with the SAME Dek().
        var owner = new JobSeekerId(Guid.NewGuid());
        using var cache = new ScopedUserDataKeyCache();
        await cache.GetOrUnwrapAsync(owner, () => Task.FromResult(Dek()), CancellationToken.None);
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(owner);
        var encryptor = new BinaryFieldEncryptor();
        var sut = new BinaryFieldOpener(cache, currentOwner, encryptor);

        var sealedContent = encryptor.Encrypt(Plaintext, Dek());
        var opened = sut.Open(sealedContent);

        opened.ShouldBe(Plaintext);
    }

    [Fact]
    public void Open_WithoutOwnerInScope_ThrowsFailClosed()
    {
        // No owner resolved (the query forgot IRequiresFieldEncryptionKey / prefetch never set the
        // owner): opening MUST throw — a stranger's plaintext is never returned.
        using var cache = new ScopedUserDataKeyCache();
        var sut = new BinaryFieldOpener(cache, new CurrentDataOwner(), new BinaryFieldEncryptor());

        var ex = Should.Throw<CryptographicException>(() => sut.Open(Plaintext));

        ex.Message.ShouldContain("IRequiresFieldEncryptionKey");
    }

    [Fact]
    public void Open_WithOwnerButColdCache_ThrowsFailClosed()
    {
        // Owner resolved but the prefetch never warmed the DEK (cold cache): opening MUST throw —
        // never a partial/unverified buffer, never a silent empty result.
        using var cache = new ScopedUserDataKeyCache();
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(new JobSeekerId(Guid.NewGuid()));
        var sut = new BinaryFieldOpener(cache, currentOwner, new BinaryFieldEncryptor());

        var ex = Should.Throw<CryptographicException>(() => sut.Open(Plaintext));

        ex.Message.ShouldContain("FieldEncryptionKeyPrefetchBehavior");
    }

    [Fact]
    public async Task Open_WithOtherOwnersDekWarmed_ThrowsFailClosed()
    {
        // Cross-owner isolation: a DEK warmed for ANOTHER owner never opens THIS owner's envelope —
        // the peek is keyed by the CURRENT owner, not "any warm DEK" (mirror of the sealer test).
        var currentOwner = new CurrentDataOwner();
        currentOwner.SetOwner(new JobSeekerId(Guid.NewGuid()));
        using var cache = new ScopedUserDataKeyCache();
        await cache.GetOrUnwrapAsync(
            new JobSeekerId(Guid.NewGuid()), // a DIFFERENT owner's DEK
            () => Task.FromResult(Dek()),
            CancellationToken.None);
        var sut = new BinaryFieldOpener(cache, currentOwner, new BinaryFieldEncryptor());

        Should.Throw<CryptographicException>(() => sut.Open(Plaintext));
    }
}
