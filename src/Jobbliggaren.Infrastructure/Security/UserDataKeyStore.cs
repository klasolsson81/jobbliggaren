using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
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
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector) : IUserDataKeyStore
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
        //
        // Förutsättning (arch-not, PR2-granskning): denna create-väg får INTE köras
        // inom en ambient transaktion på det delade AppDbContext:et. En 23505 nedan
        // skulle annars abort:a den transaktionen → re-query:n skulle fela med 25P02
        // ("current transaction is aborted"). Idag garanterat: FieldEncryptionKey-
        // PrefetchBehavior kör i ett eget pipeline-steg FÖRE UnitOfWorkBehavior, och
        // FieldEncryptionBackfiller replikerar prefetch i eget scope. En framtida
        // callsite inuti en transaktion måste re-designa recovery:n (t.ex. savepoint).
        var generated = await dataKeyProvider
            .CreateDataKeyAsync(owner, ct)
            .ConfigureAwait(false);

        var entity = new UserDataKey(
            owner,
            dekVersion: CurrentDekVersion,
            wrappedDek: generated.WrappedDek,
            cmkKeyId: generated.CmkKeyId,
            createdAt: clock.UtcNow);
        db.Set<UserDataKey>().Add(entity);

        var handedOff = false;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            handedOff = true; // anroparen/cachen äger nu generated.PlaintextDek
            return generated.PlaintextDek;
        }
        catch (DbUpdateException ex)
            when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Low2 (first-use-race): en parallell request för samma NYA ägare hann
            // före och infogade PK (job_seeker_id, dek_version). Vår Add förlorade →
            // detacha den (annars retriggar nästa SaveChanges samma failing insert)
            // och unwrappa VINNARENS rad (get-or-create-upsert, ADR 0032 §5). Vår
            // oanvända förlorar-DEK nollas i finally (returneras aldrig). Fail-closed
            // bevarat: unwrap-fel propageras, ingen fallback-DEK.
            db.Entry(entity).State = EntityState.Detached;

            var winner = await db.Set<UserDataKey>()
                .AsNoTracking()
                .Where(k => k.JobSeekerId == owner)
                .OrderByDescending(k => k.DekVersion)
                .FirstAsync(ct)
                .ConfigureAwait(false);

            EnsureSupportedVersion(winner.DekVersion);

            return await dataKeyProvider
                .UnwrapDataKeyAsync(owner, winner.WrappedDek, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            // Nyckelhygien: den genererade first-use-DEK:en ägs av anroparen ENDAST
            // på happy-return. På alla andra vägar (race-förlorare, annan
            // DbUpdateException, guard-/unwrap-fel) returneras den aldrig → nolla.
            if (!handedOff)
            {
                CryptographicOperations.ZeroMemory(generated.PlaintextDek);
            }
        }
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
