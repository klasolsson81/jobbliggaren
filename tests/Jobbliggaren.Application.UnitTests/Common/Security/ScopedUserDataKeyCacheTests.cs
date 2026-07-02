using System.Security.Cryptography;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// Low1 (audit-epik #480) — <see cref="ScopedUserDataKeyCache"/> nollar
/// plaintext-DEK-buffrar på ALLA vägar. Dispose-nollningen (success-vägen) är
/// redan täckt av UserDataKeyStoreIntegrationTests scenario 8 (Testcontainers);
/// här verifieras den tidigare o-täckta cancellation-EFTER-unwrap-FÖRE-cachning-
/// vägen, där DEK:en aldrig cachas → Dispose fångar den inte → måste nollas
/// explicit. Ren in-memory-enhet (ingen DB) via Seam 3 internal-observerbarhet
/// (<c>InternalsVisibleTo Jobbliggaren.Application.UnitTests</c>).
/// </summary>
public class ScopedUserDataKeyCacheTests
{
    private static JobSeekerId NewOwner() => new(Guid.NewGuid());

    // Determinerat icke-noll DEK-material (0xA5 i första byten garanterar att
    // "har den nollats?"-assertion:en inte falskt-passerar på en RNG-nolla).
    private static byte[] NonZeroDek()
    {
        var dek = new byte[32];
        RandomNumberGenerator.Fill(dek);
        dek[0] = 0xA5;
        return dek;
    }

    [Fact]
    public async Task GetOrUnwrap_CancelledAfterUnwrap_ZeroesUncachedDek()
    {
        using var cache = new ScopedUserDataKeyCache();
        var owner = NewOwner();
        using var cts = new CancellationTokenSource();

        // Factory:n returnerar en buffert testet håller en referens till och
        // cancellar token:en, så ThrowIfCancellationRequested fyrar EFTER unwrap
        // men FÖRE cachning. Task.FromResult wrappar SAMMA array-instans →
        // cachens ZeroMemory på cancellation-vägen syns i vår referens.
        var unwrapped = NonZeroDek();
        Task<byte[]> Factory()
        {
            cts.Cancel();
            return Task.FromResult(unwrapped);
        }

        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.GetOrUnwrapAsync(owner, Factory, cts.Token));

        // Den färsk-unwrappade DEK:en cachades aldrig (Dispose skulle inte nå
        // den) → måste ha nollats på cancellation-vägen (Low1).
        unwrapped.ShouldBe(new byte[32],
            "cancellation-EFTER-unwrap-DEK måste nollas, inte läcka o-zeroat");
        cache.TryPeekCachedDek(owner, out _).ShouldBeFalse(
            "inget cachas på cancellation-vägen (fail-closed, ingen memoisering)");
    }

    [Fact]
    public async Task GetOrUnwrap_Success_CachesAndZeroesOnDispose()
    {
        // Regressionsskydd: Low1-fixen får inte bryta success-vägen — den
        // memoiserar oförändrat och nollar cachens buffert vid dispose.
        var owner = NewOwner();
        var cache = new ScopedUserDataKeyCache();
        try
        {
            var unwrapped = NonZeroDek();
            var returned = await cache.GetOrUnwrapAsync(
                owner, () => Task.FromResult(unwrapped), CancellationToken.None);

            returned.ShouldNotBe(new byte[32], "anroparen får en icke-noll DEK-klon");
            cache.TryPeekCachedDek(owner, out var cached).ShouldBeTrue(
                "success-vägen memoiserar DEK:en inom scope");
            cached.ShouldNotBe(new byte[32], "cachad DEK är icke-noll inom scope");
        }
        finally
        {
            cache.Dispose();
        }

        cache.LastDisposedBuffersAllZeroed.ShouldBeTrue(
            "cachade plaintext-DEK-buffrar nollas vid dispose (oförändrad success-väg)");
    }
}
