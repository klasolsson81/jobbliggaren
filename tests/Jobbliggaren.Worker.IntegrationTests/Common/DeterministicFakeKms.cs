using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using NSubstitute;

namespace Jobbliggaren.Worker.IntegrationTests.Common;

/// <summary>
/// TD-13 FAS 3.5 batch C2 — Seam 1 (architect-domen 2026-05-18, Variant A).
/// Delad deterministisk NSubstitute-fake-<see cref="IAmazonKeyManagementService"/>
/// som <see cref="WorkerTestFixture"/> default-registrerar som sista-vinner
/// singleton (efter <c>AddPersistence</c>, före <c>BuildServiceProvider</c>) så
/// hela <c>_fixture.Services</c>-grafen (store + cache + KmsDataKeyProvider) kör
/// fake-KMS — produktkod orörd, ingen prod-override-yta.
///
/// <para>
/// Generate/Decrypt är deterministiska per <c>EncryptionContext["owner"]</c>-
/// seed (KmsDataKeyProvider sätter <c>owner = JobSeekerId.Value</c>) så
/// scenario 4/5/6/10/11 kan asserta isolering och reuse. <see cref="DecryptCallCount"/>
/// räknar unwrap-anrop — scenario 7 mäter cache-memoisering mot denna räknare
/// (<c>[Collection("Worker")]</c> är seriell ⇒ deterministisk).
/// </para>
/// </summary>
public sealed class DeterministicFakeKms
{
    public IAmazonKeyManagementService Substitute { get; } =
        NSubstitute.Substitute.For<IAmazonKeyManagementService>();

    private int _decryptCallCount;

    /// <summary>Antal <c>Decrypt</c>-anrop (unwrap) hittills. Trådsäker
    /// inkrementering — Worker-collection är seriell men callback:en kan köras
    /// från async-kontinuationer.</summary>
    public int DecryptCallCount => Volatile.Read(ref _decryptCallCount);

    public void ResetDecryptCount() => Interlocked.Exchange(ref _decryptCallCount, 0);

    // 32-byte AES-256-DEK-mönster (KMS-fake-plaintext), distinkt per owner-seed
    // så testerna kan asserta isolering utan kollision.
    public static byte[] FakePlaintextDek(byte seed)
    {
        var dek = new byte[32];
        for (var i = 0; i < dek.Length; i++)
            dek[i] = (byte)(seed + i);
        return dek;
    }

    public static byte[] FakeWrappedDek(byte seed) =>
        [.. Enumerable.Range(0, 80).Select(i => (byte)(0xA0 ^ (seed + i)))];

    public DeterministicFakeKms()
    {
        Substitute
            .GenerateDataKeyAsync(Arg.Any<GenerateDataKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<GenerateDataKeyRequest>();
                var seed = OwnerSeed(req.EncryptionContext);
                return Task.FromResult(new GenerateDataKeyResponse
                {
                    KeyId = "arn:aws:kms:eu-north-1:000000000000:key/td13-test-cmk",
                    Plaintext = new MemoryStream(FakePlaintextDek(seed)),
                    CiphertextBlob = new MemoryStream(FakeWrappedDek(seed)),
                });
            });

        Substitute
            .DecryptAsync(Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                Interlocked.Increment(ref _decryptCallCount);
                var req = ci.Arg<DecryptRequest>();
                var seed = OwnerSeed(req.EncryptionContext);
                return Task.FromResult(new DecryptResponse
                {
                    KeyId = "arn:aws:kms:eu-north-1:000000000000:key/td13-test-cmk",
                    Plaintext = new MemoryStream(FakePlaintextDek(seed)),
                });
            });
    }

    // KmsDataKeyProvider.EncryptionContext sätter owner = JobSeekerId.Value.
    private static byte OwnerSeed(Dictionary<string, string>? ctx)
    {
        if (ctx is null || !ctx.TryGetValue("owner", out var owner))
            return 0;
        return (byte)(Guid.Parse(owner).GetHashCode() & 0xFF);
    }
}
