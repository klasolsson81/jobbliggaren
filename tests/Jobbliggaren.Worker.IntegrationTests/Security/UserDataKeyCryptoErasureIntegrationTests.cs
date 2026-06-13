using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 FAS 3.5 batch C2 — crypto-erasure key-store-del mot riktig Postgres
/// (Testcontainers via <see cref="WorkerTestFixture"/>). ADR 0049 Beslut 2:
/// kontoradering kastar användarens DEK → backup-resident ciphertext blir
/// olesbar.
///
/// <para>
/// <b>Scope-avgränsning:</b> hela hard-delete-cascaden (JobSeeker + aggregat +
/// audit-anonymisering + Identity) testas i C6. Denna svit testar ENDAST att
/// <c>user_data_keys</c>-raderna raderas — på den nivå C2 exponerar
/// (<c>IUserDataKeyStore.DeleteDataKeysAsync</c>). Om C6:s hard-delete-hook
/// integrerar key-store-raderingen senare, blir scenario 10 ett
/// end-to-end-test i C6; C2-nivån testar repo/store-metoden direkt
/// (kontrakts-lucka #2 — se rapport).
/// </para>
///
/// <para>TDD-ordning (CLAUDE.md §2.4/§7): RÖDA tills C2-impl finns.</para>
///
/// <para>
/// <b>C2-impl-kontrakt:</b> <c>IUserDataKeyStore.DeleteDataKeysAsync(JobSeekerId,
/// CancellationToken)</c> raderar ALLA <c>user_data_keys</c>-rader för
/// JobSeekern (alla dek_version) — idempotent (ingen rad → no-op).
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class UserDataKeyCryptoErasureIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private async Task<JobSeeker> SeedJobSeekerWithDataKeyAsync(CancellationToken ct)
    {
        JobSeeker seeker;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seeker = JobSeeker.Register(
                Guid.NewGuid(), "Erasure Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);
        }

        using (var keyScope = _fixture.Services.CreateScope())
        {
            var store = keyScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct); // skapar wrapped-rad
        }

        return seeker;
    }

    private static IQueryable<UserDataKey> UserDataKeys(AppDbContext db) =>
        db.Set<UserDataKey>().AsNoTracking();

    // ── Scenario 10 ─────────────────────────────────────────────────────
    [Fact]
    public async Task HardDelete_RemovesUserDataKeyRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerWithDataKeyAsync(ct);

        // Förkrav: raden finns innan erasure.
        using (var preScope = _fixture.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await UserDataKeys(preDb).CountAsync(k => k.JobSeekerId == seeker.Id, ct))
                .ShouldBe(1, "seed: wrapped-DEK-rad ska finnas före erasure");
        }

        // Akt: C2 key-store-radering (crypto-erasure key-store-del).
        using (var eraseScope = _fixture.Services.CreateScope())
        {
            var store = eraseScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store.DeleteDataKeysAsync(seeker.Id, ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == seeker.Id, ct))
            .ShouldBe(0, "alla user_data_keys-rader ska vara borta efter crypto-erasure");
    }

    // ── Scenario 11 ─────────────────────────────────────────────────────
    [Fact]
    public async Task HardDelete_OtherUsersDataKeys_Untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var victim = await SeedJobSeekerWithDataKeyAsync(ct);
        var bystander = await SeedJobSeekerWithDataKeyAsync(ct);

        using (var eraseScope = _fixture.Services.CreateScope())
        {
            var store = eraseScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store.DeleteDataKeysAsync(victim.Id, ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == victim.Id, ct))
            .ShouldBe(0, "den raderade användarens DEK ska vara borta");
        (await UserDataKeys(db).CountAsync(k => k.JobSeekerId == bystander.Id, ct))
            .ShouldBe(1, "icke-raderad användares wrapped-DEK ska vara orörd (ADR 0049 Beslut 2 trade-off)");
    }

    // ── Idempotens (crypto-erasure måste tåla redan-raderad / aldrig-skapad) ──
    [Fact]
    public async Task HardDelete_NoDataKeyRows_IsIdempotentNoOp()
    {
        var ct = TestContext.Current.CancellationToken;

        JobSeeker seeker;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seeker = JobSeeker.Register(
                Guid.NewGuid(), "Erasure NoKey", new FixedClock(DateTimeOffset.UtcNow)).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);
        }

        // Ingen DEK skapad — DeleteDataKeysAsync ska vara no-op, ej kasta.
        using var eraseScope = _fixture.Services.CreateScope();
        var store = eraseScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();

        await Should.NotThrowAsync(async () =>
            await store.DeleteDataKeysAsync(seeker.Id, ct));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
