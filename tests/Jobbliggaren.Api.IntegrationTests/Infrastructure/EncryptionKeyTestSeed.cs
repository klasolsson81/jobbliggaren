using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// TD-13 C3 — delad test-seed-warm för fält-krypteringens ägar-DEK
/// (en sanning, DRY). Speglar
/// <c>FieldEncryptionInterceptorTests.PrefetchOwnerDekAsync</c> i Worker-
/// sviten verbatim: simulerar <see cref="Jobbliggaren.Application.Common.Behaviors"/>:s
/// <c>FieldEncryptionKeyPrefetchBehavior</c> i ett scope som seedar
/// Application/ApplicationNote/FollowUp DIREKT via DbContext (förbi
/// Mediator-pipelinen).
///
/// <para>
/// <b>Varför detta behövs:</b> produktionskoden är korrekt — alla prod-writes
/// går via Mediator-markerade commands som prefetch:ar ägar-DEK i ett eget
/// pipeline-steg före UnitOfWork. Api-integ-tester som seedar krypterade fält
/// (cover_letter / application_notes.content / follow_ups.note) direkt via
/// <c>db.Applications.Add(...)</c> + <c>SaveChangesAsync()</c> kör inte
/// pipelinen, så <c>FieldEncryptionSaveChangesInterceptor</c> fail-closed:ar
/// ("ingen cachad DEK"). Denna helper värmer ägar-DEK i scopets
/// <c>ScopedUserDataKeyCache</c> + sätter <see cref="ICurrentDataOwner"/>
/// exakt som behaviorn gör, så interceptor-paret blir rena synkrona
/// cache-konsumenter.
/// </para>
///
/// <para>
/// <b>KRITISK ordning:</b> anropa <see cref="WarmAsync"/> i SAMMA scope som
/// seed-SaveChanges, FÖRE <c>db.Applications.Add(...)</c>.
/// <c>UserDataKeyStore.ResolveDekAsync</c> gör en egen
/// <c>SaveChangesAsync</c> när den persisterar user_data_keys-raden; om det
/// finns pending krypterade entiteter i ChangeTrackern flushas de då med tom
/// cache → fail-closed. Värm alltså FÖRE entiteterna läggs till.
/// </para>
///
/// <para>
/// <b>FK:</b> <c>UserDataKeyStore</c> kräver bara JobSeekerId-värdet för
/// KMS-context + en user_data_keys-rad (ingen FK-check i koden). Men
/// FK ON DELETE CASCADE finns mot job_seekers — seeda JobSeeker före
/// <see cref="WarmAsync"/> om testet gör det (mönstret i de berörda
/// SeedAsync-helpers lägger JobSeeker före warm naturligt).
/// </para>
/// </summary>
internal static class EncryptionKeyTestSeed
{
    /// <summary>
    /// Värmer ägar-DEK i <paramref name="scope"/>:s scoped cache + sätter
    /// <see cref="ICurrentDataOwner"/>. Måste anropas FÖRE krypterade
    /// entiteter läggs till / sparas i samma scope.
    /// </summary>
    public static async Task WarmAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek); // cachen äger sin egen kopia
    }
}
