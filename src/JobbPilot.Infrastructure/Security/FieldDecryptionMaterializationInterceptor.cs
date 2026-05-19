using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 + Mekanik-not 1/3/4) — decrypt-on-read. Ren
/// synkron cache-hit: <c>FieldEncryptionKeyPrefetchBehavior</c> har redan värmt
/// ägar-DEK i <see cref="ScopedUserDataKeyCache"/> (samma scope) → ingen
/// async/KMS-I/O här (§3.5). Ägare: <see cref="Application"/> via
/// <c>JobSeekerId</c>; barn via <see cref="ICurrentDataOwner"/> (request är
/// owner-scoped per query). Legacy klartext (ingen sentinel) lämnas orört
/// (lazy-tolerans, Beslut 4). Fail-closed: krypterat värde utan
/// resolverbar/cachad DEK → kastar (returnerar aldrig ciphertext oläst).
///
/// <para>Mekanik-not 5c: <b>singleton</b>-interceptor (EF
/// <c>ISingletonInterceptor</c>). Scoped state resolvas via
/// <c>materializationData.Context.GetService&lt;T&gt;()</c> vid invocation
/// (samma scope som AppDbContext), INTE via konstruktor — annars varierande
/// instans per scope → EF ServiceProviderCache-explosion. Stateless/trådsäker
/// (PropertyCache är statisk + immutabel-nyckel).</para>
/// </summary>
public sealed class FieldDecryptionMaterializationInterceptor : IMaterializationInterceptor
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> PropertyCache = [];

    public object InitializedInstance(
        MaterializationInterceptionData materializationData, object entity)
    {
        var type = entity.GetType();

        // Form A — C3 TEXT-kolumner: domän-string-property dekrypteras in-place.
        if (EncryptedFieldRegistry.TryGetEncryptedProperties(type, out var properties))
        {
            var context = materializationData.Context;
            var fieldEncryptor = context.GetService<IFieldEncryptor>();
            var cache = context.GetService<ScopedUserDataKeyCache>();
            var currentDataOwner = context.GetService<ICurrentDataOwner>();

            JobSeekerId? owner = entity is DomainApplication app
                ? app.JobSeekerId
                : currentDataOwner.JobSeekerId;

            foreach (var propertyName in properties)
            {
                var property = PropertyCache.GetOrAdd(
                    (type, propertyName),
                    key => key.Item1.GetProperty(
                        key.Item2,
                        BindingFlags.Public | BindingFlags.Instance)!);

                if (property.GetValue(entity) is not string value
                    || value.Length == 0
                    || !fieldEncryptor.IsEncrypted(value))
                {
                    // null / tom / legacy-klartext (ingen sentinel) → orört.
                    continue;
                }

                if (owner is null || !cache.TryPeekCachedDek(owner.Value, out var dek))
                {
                    // CTO #3 (iv) 2026-05-18, Mekanik-not 5: scope-
                    // differentierad fail-closed. Autentiserad ägar-scope
                    // (prefetch förväntades ha kört) → kasta (felkonfig-
                    // användar-read får ALDRIG tyst ciphertext). System/
                    // Hangfire-scope (ingen ICurrentDataOwner, ingen auth —
                    // t.ex. MarkGhosted/AccountHardDeleter som materialiserar
                    // men aldrig läser plaintext-fältet) → lämna ciphertext
                    // orört, kasta INTE (drift får ej krascha; konfidentialitet
                    // bevarad; encrypt-interceptorn idempotent-skippar re-save).
                    // Arch-test spärrar system-commands från att läsa fältet.
                    if (currentDataOwner.JobSeekerId is not null)
                    {
                        throw new CryptographicException(
                            $"FieldDecryption: ingen cachad DEK för {type.Name}.{propertyName} " +
                            "— FieldEncryptionKeyPrefetchBehavior måste ha värmt ägar-DEK " +
                            "(ADR 0049 Mekanik-not 3/4/5).");
                    }

                    continue;
                }

                property.SetValue(entity, fieldEncryptor.Decrypt(value, dek));
            }

            return entity;
        }

        // Form B — C4 #1c (ADR 0049 Mekanik-not 6): krypterad text-shadow
        // läses via MaterializationInterceptionData.GetPropertyValue (C4.2a-
        // gate GREEN: fungerar under AsNoTracking — ingen ChangeTracker-entry
        // krävs). content_enc(sentinel) → decrypt → JSON → VO. Backfill-
        // fallback (Beslut 5 steg 2): content_enc null/ej-sentinel → legacy
        // klartext-jsonb (ingen decrypt, ingen DEK, alla scopes).
        if (EncryptedFieldRegistry.TryGetJsonSerializedFields(type, out var jsonFields))
        {
            var context = materializationData.Context;
            var fieldEncryptor = context.GetService<IFieldEncryptor>();
            var cache = context.GetService<ScopedUserDataKeyCache>();
            var currentDataOwner = context.GetService<ICurrentDataOwner>();

            foreach (var field in jsonFields)
            {
                var enc = materializationData.GetPropertyValue<string>(field.ShadowProperty);

                if (enc is { Length: > 0 } && fieldEncryptor.IsEncrypted(enc))
                {
                    var owner = currentDataOwner.JobSeekerId;
                    if (owner is null || !cache.TryPeekCachedDek(owner.Value, out var dek))
                    {
                        // Scope-differentierad fail-closed (Mekanik-not 5b
                        // ärvd). ResumeVersion saknar egen JobSeekerId →
                        // owner == currentDataOwner.JobSeekerId. Autentiserad
                        // scope utan cachad DEK → kasta. System-scope (ingen
                        // owner) → passthrough: Content förblir null (aldrig
                        // ciphertext-exponering; arch-test spärrar system-
                        // läsning av Content).
                        if (currentDataOwner.JobSeekerId is not null)
                        {
                            throw new CryptographicException(
                                $"FieldDecryption: ingen cachad DEK för " +
                                $"{type.Name}.{field.DomainProperty} — " +
                                "FieldEncryptionKeyPrefetchBehavior måste ha värmt " +
                                "ägar-DEK (ADR 0049 Mekanik-not 3/4/5/6).");
                        }

                        continue;
                    }

                    SetDomainProperty(
                        type, field, entity, fieldEncryptor.Decrypt(enc, dek));
                    continue;
                }

                // Backfill-fallback: legacy klartext-JSON (ingen sentinel) →
                // ingen decrypt/DEK/owner (lazy-tolerans, Beslut 4/5; alla
                // scopes inkl. system).
                var legacy = materializationData.GetPropertyValue<string>(
                    field.LegacyShadowProperty);
                if (legacy is { Length: > 0 })
                {
                    SetDomainProperty(type, field, entity, legacy);
                }
            }

            return entity;
        }

        return entity;
    }

    private static void SetDomainProperty(
        Type type, JsonSerializedVoField field, object entity, string json)
    {
        var property = PropertyCache.GetOrAdd(
            (type, field.DomainProperty),
            key => key.Item1.GetProperty(
                key.Item2,
                BindingFlags.Public | BindingFlags.Instance)!);
        property.SetValue(entity, field.FromJson(json));
    }
}
