using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Cryptography;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 + Mekanik-not 1/5, CTO Approach A 2026-05-18) —
/// encrypt-on-write som <b>ren synkron cache-konsument</b> (speglar
/// <see cref="FieldDecryptionMaterializationInterceptor"/>). Anropar ALDRIG
/// <c>IUserDataKeyStore</c>/KMS inifrån SaveChanges — det orsakade
/// re-entrant <c>SaveChangesAsync</c> på samma DbContext → EF
/// concurrency-detector-deadlock (Mekanik-not 5). DEK värms istället av
/// <c>FieldEncryptionKeyPrefetchBehavior</c> i ett EGET pipeline-steg före
/// UnitOfWork; interceptorn läser bara den scope-cachade DEK:en synkront.
///
/// Krypterar allowlist:ade fält på Added/Modified-entiteter FÖRE Npgsql-DML.
/// Ägar-DEK: <see cref="Application"/> via <c>JobSeekerId</c>; barn via
/// skugg-FK → spårad parent. Idempotent (redan sentinel-prefixat hoppas);
/// <c>null</c>/tom hoppas. Fail-closed: plaintext att kryptera men ingen
/// cachad DEK → <see cref="CryptographicException"/> FÖRE DML → hela
/// SaveChanges rullas, ingen klartext persisteras (CTO lucka 5). Cachen
/// äger DEK-bufferten — interceptorn nollar den ALDRIG (dispose-nollas).
///
/// <para>Mekanik-not 5c: <b>singleton</b>-interceptor (EF
/// <c>ISingletonInterceptor</c>). Scoped state (<see cref="IFieldEncryptor"/>,
/// <see cref="ScopedUserDataKeyCache"/>) resolvas via
/// <c>eventData.Context.GetService&lt;T&gt;()</c> vid invocation (samma scope
/// som AppDbContext = samma scope som prefetch-behaviorn värmde), INTE via
/// konstruktor — annars varierande instans per scope → EF
/// ServiceProviderCache-explosion (<c>ManyServiceProvidersCreatedWarning</c>).
/// Stateless → trådsäker.</para>
/// </summary>
public sealed class FieldEncryptionSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        EncryptInPlace(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EncryptInPlace(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void EncryptInPlace(DbContext? context)
    {
        if (context is null)
            return;

        // Resolva scoped state från DbContext-scopet (singleton-interceptor —
        // ej ctor-injektion; Mekanik-not 5c).
        var fieldEncryptor = context.GetService<IFieldEncryptor>();
        var cache = context.GetService<ScopedUserDataKeyCache>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (!EncryptedFieldRegistry.TryGetEncryptedProperties(
                    entry.Entity.GetType(), out var properties))
                continue;

            JobSeekerId? owner = null;

            foreach (var propertyName in properties)
            {
                var property = entry.Property(propertyName);
                if (property.CurrentValue is not string plaintext
                    || plaintext.Length == 0
                    || fieldEncryptor.IsEncrypted(plaintext))
                {
                    // null / tom / redan krypterad → idempotent skip.
                    // (System-jobb som re-sparar ciphertext träffar denna —
                    //  ingen DEK-lookup, ingen krasch.)
                    continue;
                }

                owner ??= ResolveOwner(entry, context);

                // Ren synkron cache-hit (FieldEncryptionKeyPrefetchBehavior
                // har värmt DEK i sitt eget pipeline-steg före UnitOfWork).
                // Fail-closed: plaintext men ingen cachad DEK → kasta FÖRE
                // DML (hela SaveChanges rullas). Cachen äger bufferten.
                if (!cache.TryPeekCachedDek(owner.Value, out var dek))
                {
                    throw new CryptographicException(
                        $"FieldEncryption: ingen cachad DEK för " +
                        $"{entry.Entity.GetType().Name}.{propertyName} — " +
                        "FieldEncryptionKeyPrefetchBehavior måste ha värmt " +
                        "ägar-DEK före write (ADR 0049 Mekanik-not 3/5).");
                }

                property.CurrentValue = fieldEncryptor.Encrypt(plaintext, dek);
            }
        }
    }

    private static JobSeekerId ResolveOwner(EntityEntry entry, DbContext context)
    {
        if (entry.Entity is DomainApplication application)
            return application.JobSeekerId;

        // Barn (ApplicationNote/FollowUp): skugg-FK → spårad parent.
        // Barn kan ej persisteras utan aggregatrot (internal factory-
        // invariant) → parent garanterat i ChangeTracker i alla legitima
        // skrivvägar. Saknas parent = aggregat-brott → fail-closed.
        var fk = entry.Property("ApplicationId").CurrentValue;
        foreach (var appEntry in context.ChangeTracker.Entries<DomainApplication>())
        {
            var id = appEntry.Entity.Id;
            if ((fk is JobbPilot.Domain.Applications.ApplicationId aid && id == aid)
                || (fk is Guid g && id.Value == g))
            {
                return appEntry.Entity.JobSeekerId;
            }
        }

        throw new InvalidOperationException(
            $"FieldEncryption: barn-entitet {entry.Entity.GetType().Name} saknar " +
            "spårad parent Application — ägar-DEK kan ej resolvas (aggregat-brott).");
    }
}
