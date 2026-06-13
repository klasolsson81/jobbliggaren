using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using NSubstitute;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// TD-13 C3 — deterministisk fake-<see cref="IAmazonKeyManagementService"/>
/// för Api-integ. C3:s interceptor-par anropar KMS (GenerateDataKey/Decrypt)
/// vid Application-writes/reads i full Mediator-pipeline. ApiFactory MÅSTE
/// faka KMS (annars riktig AWS-KMS-anrop med tom CMK → fail). Samma seam-
/// mönster som WorkerTestFixture (C2 Seam 1, architect-domen Variant A) —
/// produktkod orörd, sista-vinner-singleton i ConfigureServices.
///
/// Generate/Decrypt deterministiska per <c>EncryptionContext["owner"]</c>
/// (KmsDataKeyProvider sätter owner = JobSeekerId.Value) → round-trip korrekt
/// + per-användare-isolering bevarad i end-to-end-tester.
/// </summary>
internal static class ApiKmsFake
{
    public static IAmazonKeyManagementService Create()
    {
        var kms = Substitute.For<IAmazonKeyManagementService>();

        kms.GenerateDataKeyAsync(
                Arg.Any<GenerateDataKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var seed = OwnerSeed(ci.Arg<GenerateDataKeyRequest>().EncryptionContext);
                return Task.FromResult(new GenerateDataKeyResponse
                {
                    KeyId = "arn:aws:kms:eu-north-1:000000000000:key/api-test-cmk",
                    Plaintext = new MemoryStream(PlaintextDek(seed)),
                    CiphertextBlob = new MemoryStream(WrappedDek(seed)),
                });
            });

        kms.DecryptAsync(Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var seed = OwnerSeed(ci.Arg<DecryptRequest>().EncryptionContext);
                return Task.FromResult(new DecryptResponse
                {
                    KeyId = "arn:aws:kms:eu-north-1:000000000000:key/api-test-cmk",
                    Plaintext = new MemoryStream(PlaintextDek(seed)),
                });
            });

        return kms;
    }

    private static byte OwnerSeed(Dictionary<string, string>? ctx) =>
        ctx is not null && ctx.TryGetValue("owner", out var owner)
            ? (byte)(Guid.Parse(owner).GetHashCode() & 0xFF)
            : (byte)0;

    private static byte[] PlaintextDek(byte seed)
    {
        var dek = new byte[32];
        for (var i = 0; i < dek.Length; i++)
            dek[i] = (byte)(seed + i);
        return dek;
    }

    private static byte[] WrappedDek(byte seed) =>
        [.. Enumerable.Range(0, 80).Select(i => (byte)(0xA0 ^ (seed + i)))];
}
