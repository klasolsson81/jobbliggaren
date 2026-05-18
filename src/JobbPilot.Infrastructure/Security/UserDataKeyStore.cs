using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Infrastructure.Security;

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
    public Task<byte[]> GetOrCreateDataKeyAsync(JobSeekerId owner, CancellationToken ct) =>
        cache.GetOrUnwrapAsync(owner, () => ResolveDekAsync(owner, ct), ct);

    private async Task<byte[]> ResolveDekAsync(JobSeekerId owner, CancellationToken ct)
    {
        // Senaste DEK-versionen för användaren (rotation-redo, ADR 0049 Beslut 4).
        var existing = await db.Set<UserDataKey>()
            .Where(k => k.JobSeekerId == owner)
            .OrderByDescending(k => k.DekVersion)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Fail-closed: KMS-fel propageras (ingen fallback-DEK).
            return await dataKeyProvider
                .UnwrapDataKeyAsync(owner, existing.WrappedDek, ct)
                .ConfigureAwait(false);
        }

        // Första behovet: skapa + persistera wrapped-DEK (version 1).
        var generated = await dataKeyProvider
            .CreateDataKeyAsync(owner, ct)
            .ConfigureAwait(false);

        db.Set<UserDataKey>().Add(new UserDataKey(
            owner,
            dekVersion: 1,
            wrappedDek: generated.WrappedDek,
            cmkKeyId: generated.CmkKeyId,
            createdAt: clock.UtcNow));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return generated.PlaintextDek;
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
