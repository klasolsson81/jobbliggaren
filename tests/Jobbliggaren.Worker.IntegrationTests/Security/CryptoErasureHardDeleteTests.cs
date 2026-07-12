using System.Data.Common;
using System.Security.Cryptography;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 FAS 3.5 batch <b>C6 — crypto-erasure-hook i hard-delete-cascaden</b>
/// (ADR 0049 Beslut 2 + C6, GDPR Art. 17). Mot riktig Postgres + AspNet
/// Identity (Testcontainers via <see cref="WorkerTestFixture"/>) — InMemory
/// förbjudet (CLAUDE.md/test-stack): atomiciteten mellan aggregat-delete och
/// <c>user_data_keys</c>-radering vilar på att <c>DeleteDataKeysAsync</c>
/// (ExecuteDeleteAsync) enlistar i den ambienta <c>BeginTransactionAsync</c>-
/// transaktionen — empiriskt verifierbart enbart mot en riktig provider.
///
/// <para>
/// <b>Scope-avgränsning vs systerns sviter:</b>
/// <see cref="UserDataKeyCryptoErasureIntegrationTests"/> (C2) testar
/// <c>IUserDataKeyStore.DeleteDataKeysAsync</c> ISOLERAT;
/// <see cref="Jobbliggaren.Worker.IntegrationTests.Auth.HardDeleteAccountsJobIntegrationTests"/>
/// testar hard-delete-cascaden men asserterar INTE på <c>user_data_keys</c>.
/// Denna svit fyller C6-luckan: crypto-erasure END-TO-END genom
/// <see cref="IAccountHardDeleter.HardDeleteAccountAsync"/> — DEK-raderna OCH
/// aggregaten ska vara borta i SAMMA committade transaktion. Duplicerar
/// medvetet INTE hela hard-delete-sviten (audit-anonymisering/Identity testas
/// där) — enbart DEK-erasure-invarianten adderas.
/// </para>
///
/// <para>
/// <b>Rollback-atomicitet (scenario 9):</b> att injicera ett fel exakt EFTER
/// <c>DeleteDataKeysAsync</c> men FÖRE <c>CommitAsync</c> kräver en sömfog som
/// produktkoden inte exponerar (felinjektion vore test-only-produkt-yta —
/// förbjudet, jfr C3/C4-FailingProviderGraph-disciplinen). Atomiciteten är
/// architect-verifierad 2026-05-19 (ExecuteDeleteAsync enlistar i ambient tx,
/// catch ⇒ RollbackAsync ⇒ throw). Vi testar därför den observerbara
/// konsekvensen: vid LYCKAD delete är BÅDE DEK-rader OCH aggregat borta
/// (positiv atomicitet), och på icke-existerande JobSeeker är det idempotent
/// no-op utan throw (negativ — ingen partiell mutation).
/// </para>
///
/// <para>TDD-ordning (CLAUDE.md §2.4/§7): linjerad mot färdig C6-produktkod
/// on-disk 2026-05-19 (AccountHardDeleter ctor injicerar IUserDataKeyStore;
/// DeleteDataKeysAsync inom BeginTransactionAsync-try-blocket mellan
/// JobSeekers.Remove och SaveChangesAsync). Specifikationstest mot
/// kontrakts-ytan.</para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CryptoErasureHardDeleteTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private const int RestoreWindowDays = 30;

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static IQueryable<UserDataKey> UserDataKeys(AppDbContext db) =>
        db.Set<UserDataKey>().AsNoTracking();

    /// <summary>
    /// Simulerar <c>FieldEncryptionKeyPrefetchBehavior</c> i write-scopet.
    /// </summary>
    private static async Task PrefetchOwnerDekAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek);
    }

    /// <summary>
    /// Seedar en soft-deletad JobSeeker (utanför restore-fönstret, redo för
    /// hard-delete) med krypterad PII (Application.CoverLetter via interceptorn)
    /// + en wrapped-DEK-rad (skapas av <see cref="PrefetchOwnerDekAsync"/>).
    /// </summary>
    private async Task<(Guid UserId, JobSeekerId JobSeekerId, ApplicationId AppId)>
        SeedSoftDeletedAccountWithEncryptedPiiAndDekAsync(CancellationToken ct)
    {
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-(RestoreWindowDays + 1));

        Guid userId;
        JobSeekerId jsId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var email = $"ce-{Guid.NewGuid():N}@test.local";
            var user = new ApplicationUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, "CryptoErasurePass123!"))
                .Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");
            userId = user.Id;

            var seeker = JobSeeker.Register(
                user.Id, "CryptoErasure Seed",
                new FixedClock(deletedAt.AddDays(-1))).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);
            jsId = seeker.Id;
        }

        // Skriv krypterad PII (cover_letter via SaveChanges-interceptorn —
        // kräver varm DEK i write-scopet → skapar även wrapped-DEK-raden).
        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, jsId, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = DomainApplication.Create(
                jsId, jobAdId: null,
                coverLetter: "Krypterad PII som ska crypto-erasas",
                manualPosting: null,
                new FixedClock(DateTimeOffset.UtcNow)).Value;
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        // Soft-delete (utanför restore-fönstret) i eget scope.
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeker = await db.JobSeekers
                .IgnoreQueryFilters()
                .SingleAsync(js => js.Id == jsId, ct);
            seeker.SoftDelete(new FixedClock(deletedAt));
            await db.SaveChangesAsync(ct);
        }

        return (userId, jsId, appId);
    }

    private async Task HardDeleteAsync(JobSeekerId jsId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var hardDeleter = scope.ServiceProvider.GetRequiredService<IAccountHardDeleter>();
        await hardDeleter.HardDeleteAccountAsync(jsId.Value, ct);
    }

    // ── 8. Crypto-erasure-atomicitet — DEK-rader + aggregat borta ────────
    [Fact]
    public async Task HardDelete_EncryptedAccount_RemovesDataKeysAndAggregates()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, jsId, appId) =
            await SeedSoftDeletedAccountWithEncryptedPiiAndDekAsync(ct);

        // Förkrav: wrapped-DEK-rad + aggregat finns FÖRE hard-delete.
        using (var preScope = _fixture.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await UserDataKeys(preDb).CountAsync(k => k.JobSeekerId == jsId, ct))
                .ShouldBe(1, "seed: wrapped-DEK-rad ska finnas före erasure");
            (await preDb.Applications.IgnoreQueryFilters()
                .CountAsync(a => a.Id == appId, ct))
                .ShouldBe(1, "seed: krypterad Application ska finnas");

            var rawCoverLetter = await RawScalarAsync(
                preDb,
                $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
                ct);
            rawCoverLetter.ShouldNotBeNull(
                "seed-förkrav: PII ska vara on-disk-ciphertext (inte klartext)");
            // Shouldly 4.3: ShouldStartWith(string, X) tolkar 2:a arg som Case
            // (ingen customMessage-overload) — paritet med C3/C4.4-filerna.
            rawCoverLetter.ShouldStartWith("v1:");
        }

        await HardDeleteAsync(jsId, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == jsId, ct))
            .ShouldBe(0,
                "user_data_keys-rader ska vara borta efter crypto-erasure " +
                "(ADR 0049 Beslut 2 — backup-resident ciphertext blir olesbar)");
        (await db.JobSeekers.IgnoreQueryFilters()
            .CountAsync(js => js.Id == jsId, ct))
            .ShouldBe(0, "JobSeeker-aggregatet ska vara hard-deletat");
        (await db.Applications.IgnoreQueryFilters()
            .CountAsync(a => a.Id == appId, ct))
            .ShouldBe(0,
                "krypterad Application ska vara borta i SAMMA committade tx " +
                "som DEK-raden (atomisk Art. 17-erasure)");
    }

    // ── 9. Atomicitet — lyckad delete: båda borta; saknad: idempotent ────
    [Fact]
    public async Task HardDelete_SuccessfulDelete_DekAndAggregatesBothGone_NonExistentIsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;

        // (a) Positiv atomicitet: lyckad delete ⇒ BÅDE DEK OCH aggregat borta.
        var (_, jsId, appId) =
            await SeedSoftDeletedAccountWithEncryptedPiiAndDekAsync(ct);
        await HardDeleteAsync(jsId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == jsId, ct))
                .ShouldBe(0);
            (await db.Applications.IgnoreQueryFilters()
                .CountAsync(a => a.Id == appId, ct)).ShouldBe(0);
            (await db.JobSeekers.IgnoreQueryFilters()
                .CountAsync(js => js.Id == jsId, ct)).ShouldBe(0);
        }

        // (b) Negativ: icke-existerande JobSeeker ⇒ idempotent no-op, ingen
        // throw, ingen partiell mutation (AccountHardDeleter early-return).
        var ghostId = new JobSeekerId(Guid.NewGuid());
        await Should.NotThrowAsync(async () => await HardDeleteAsync(ghostId, ct));
    }

    // ── 10. Idempotens — JobSeeker UTAN DEK-rad ⇒ ingen throw ────────────
    [Fact]
    public async Task HardDelete_NoUserDataKeyRow_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-(RestoreWindowDays + 1));

        JobSeekerId jsId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var email = $"ce-nokey-{Guid.NewGuid():N}@test.local";
            var user = new ApplicationUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, "NoKeyPass123!"))
                .Succeeded.ShouldBeTrue();

            // Registrera + soft-delete UTAN att någonsin skapa en DEK
            // (ingen PII skriven ⇒ ingen GetOrCreateDataKeyAsync).
            var seeker = JobSeeker.Register(
                user.Id, "CryptoErasure NoKey",
                new FixedClock(deletedAt.AddDays(-1))).Value;
            seeker.SoftDelete(new FixedClock(deletedAt));
            seedDb.JobSeekers.Add(seeker);
            await seedDb.SaveChangesAsync(ct);
            jsId = seeker.Id;
        }

        using (var preScope = _fixture.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await UserDataKeys(preDb).CountAsync(k => k.JobSeekerId == jsId, ct))
                .ShouldBe(0, "förkrav: ingen DEK-rad skapad för denna ägare");
        }

        // DeleteDataKeysAsync med 0 rader = no-op ⇒ hela hard-delete ska
        // lyckas utan throw.
        await Should.NotThrowAsync(async () => await HardDeleteAsync(jsId, ct));

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.JobSeekers.IgnoreQueryFilters()
            .CountAsync(js => js.Id == jsId, ct))
            .ShouldBe(0, "JobSeeker ska hard-deletas även utan DEK-rad");
    }

    // ── 11. Cross-user-säkerhet — endast offrets DEK raderas ─────────────
    [Fact]
    public async Task HardDelete_CrossUser_OnlyVictimDekRowsDeleted_BystanderIntact()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, victimId, _) =
            await SeedSoftDeletedAccountWithEncryptedPiiAndDekAsync(ct);
        var (_, bystanderId, bystanderAppId) =
            await SeedSoftDeletedAccountWithEncryptedPiiAndDekAsync(ct);

        await HardDeleteAsync(victimId, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == victimId, ct))
            .ShouldBe(0, "offrets DEK-rad ska vara borta");
        (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == bystanderId, ct))
            .ShouldBe(1,
                "icke-raderad användares wrapped-DEK ska vara orörd " +
                "(ADR 0049 Beslut 2 — per-användare-DEK-isolering)");
        (await db.Applications.IgnoreQueryFilters()
            .CountAsync(a => a.Id == bystanderAppId, ct))
            .ShouldBe(1, "bystanderns krypterade Application ska vara orörd");
    }

    // ── Hjälpare ────────────────────────────────────────────────────────

    private static async Task<string?> RawScalarAsync(
        AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : raw.ToString();
    }
}
