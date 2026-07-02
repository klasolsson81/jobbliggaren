using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1) — <see cref="IUserDataKeyStore"/>-impl.
/// Använder konkret <see cref="AppDbContext"/> via <c>Set&lt;UserDataKey&gt;()</c>
/// (FRÅGA 2 — <c>UserDataKey</c> exponeras aldrig via <c>IAppDbContext</c>).
/// Scoped: delar scopets <see cref="AppDbContext"/> så
/// <see cref="DeleteDataKeysAsync"/> deltar i hard-delete-transaktionen (C6).
/// DEK-unwrap memoiseras per scope via <see cref="IUserDataKeyCache"/>.
/// </summary>
public sealed class UserDataKeyStore(
    AppDbContext db,
    IDataKeyProvider dataKeyProvider,
    IUserDataKeyCache cache,
    IDateTimeProvider clock) : IUserDataKeyStore
{
    // #501: single-version-invariant. DEK-versionen och sentinel-prefixet
    // (FieldEncryptionSentinel.VersionPrefix = "v1:") är låsta till SAMMA version.
    // Att bumpa den ena utan den andra + en versionsmedveten läsväg + en
    // re-encrypt-migration bricker befintlig ciphertext (#501/TD-102).
    private const int CurrentDekVersion = 1;

    public Task<byte[]> GetOrCreateDataKeyAsync(JobSeekerId owner, CancellationToken ct) =>
        cache.GetOrUnwrapAsync(owner, () => ResolveDekAsync(owner, ct), ct);

    private async Task<byte[]> ResolveDekAsync(JobSeekerId owner, CancellationToken ct)
    {
        // #501 (Medium): läsvägen är versionsblind — denna query väljer högsta
        // DekVersion, Encrypt hårdkodar "v1:" och Decrypt avvisar allt utom v1.
        // Korrekt så länge exakt EN v1-rad finns per ägare. I det ögonblick
        // rotationskod infogar en (owner, dek_version=2)-rad skulle detta returnera
        // v2-DEK:en och dekryptera all v1-ciphertext med fel nyckel → AES-GCM-
        // tag-mismatch på VARJE läsning (hela datasetet oläsbart). Single-version-
        // invarianten hålls därför av en hård guard nedan; äkta rotation kräver
        // versionsmedveten (owner, version)-resolution + re-encrypt-migration (ej
        // TD-102:s master-nyckel-re-wrap, som behåller dek_version). Se #501/TD-102.
        var existing = await db.Set<UserDataKey>()
            .Where(k => k.JobSeekerId == owner)
            .OrderByDescending(k => k.DekVersion)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            EnsureSupportedVersion(existing.DekVersion);

            // Fail-closed: KMS-fel propageras (ingen fallback-DEK).
            return await dataKeyProvider
                .UnwrapDataKeyAsync(owner, existing.WrappedDek, ct)
                .ConfigureAwait(false);
        }

        // Första behovet: skapa + persistera wrapped-DEK (v1).
        var generated = await dataKeyProvider
            .CreateDataKeyAsync(owner, ct)
            .ConfigureAwait(false);

        db.Set<UserDataKey>().Add(new UserDataKey(
            owner,
            dekVersion: CurrentDekVersion,
            wrappedDek: generated.WrappedDek,
            cmkKeyId: generated.CmkKeyId,
            createdAt: clock.UtcNow));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return generated.PlaintextDek;
    }

    // #501: hård single-version-guard. En dek_version != CurrentDekVersion betyder
    // att rotationskod infogats utan versionsmedveten läsväg + migration → kasta
    // loud (fail-closed) i stället för att tyst dekryptera med fel DEK. Lyft först
    // när (owner, version)-resolution + sentinel→version-matchning + re-encrypt-
    // migration finns (#501/TD-102).
    private static void EnsureSupportedVersion(int dekVersion)
    {
        if (dekVersion != CurrentDekVersion)
        {
            throw new CryptographicException(
                $"UserDataKeyStore: dek_version {dekVersion} stöds inte — endast " +
                $"v{CurrentDekVersion} (versionsmedveten läsväg + re-encrypt-" +
                "migration krävs för rotation, #501/TD-102).");
        }
    }

    public async Task DeleteDataKeysAsync(JobSeekerId owner, CancellationToken ct)
    {
        // Crypto-erasure (ADR 0049 Beslut 2). ExecuteDeleteAsync deltar i den
        // ambient hard-delete-transaktionen (C6, AccountHardDeleter) → atomär
        // med aggregat-delete. Idempotent: 0 rader = no-op, kastar ej.
        await db.Set<UserDataKey>()
            .Where(k => k.JobSeekerId == owner)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
