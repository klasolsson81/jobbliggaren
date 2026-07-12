using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Security;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 FAS 3.5 batch C2 — key-store + scoped DEK-cache mot riktig Postgres
/// (Testcontainers via <see cref="WorkerTestFixture"/>). ADR 0049 Beslut 1
/// (per-användare-DEK), CTO-triage FRÅGA 2 (<c>user_data_keys</c> keyless på
/// AppDbContext) + C1-gate security Minor 2 (PlaintextDek-zeroing måste bevisas
/// i konsumtionen).
///
/// <para>
/// <b>Seam 1 (architect-domen 2026-05-18, Variant A; ADR 0066/#802):</b>
/// DEK-providern är ALLTID den riktiga <c>LocalDataKeyProvider</c> lindad i den
/// räknande <see cref="CountingDataKeyProvider"/> som
/// <see cref="WorkerTestFixture"/> sista-vinner-registrerar för hela grafen —
/// ingen AWS, ingen prod-override-yta, produktkod orörd. Scenario 7 mäter
/// unwrap-count mot dekoratörens räknare (Worker-collection seriell ⇒
/// deterministiskt). Scenario 9 (fail-closed) direkt-konstruerar ensamt
/// store+cache+failing-provider (husets HardDeleteAccountsJob-precedens —
/// fail-closed-vägen behöver ej DbContext-grafens äkthet, bara att store kastar
/// + cachen är tom). Postgres är riktig (<c>user_data_keys</c> är en riktig
/// tabell — InMemory förbjudet, CLAUDE.md/test-stack).
/// </para>
///
/// <para>
/// <b>Seam 3 (architect-domen):</b> introspektion (unwrap-count, peek, zeroed-
/// flagga) är <c>internal</c> på konkreta <see cref="ScopedUserDataKeyCache"/>,
/// EJ på <see cref="IUserDataKeyCache"/>-porten. Scenario 7/8 resolvar porten
/// ur scope och castar till konkreta typen (<c>[InternalsVisibleTo]</c> finns).
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class UserDataKeyStoreIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "DEK Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    // Läser user_data_keys keyless via AppDbContext.Set<UserDataKey>()
    // (FRÅGA 2: EJ via IAppDbContext).
    private static IQueryable<UserDataKey> UserDataKeys(AppDbContext db) =>
        db.Set<UserDataKey>().AsNoTracking();

    // Seam 3: resolvar prod-porten ur scope, castar till konkreta typen för
    // att nå internal test-observerbarhet (ej på IUserDataKeyCache).
    private static ScopedUserDataKeyCache ConcreteCache(IServiceScope scope) =>
        (ScopedUserDataKeyCache)scope.ServiceProvider.GetRequiredService<IUserDataKeyCache>();

    // ── Scenario 4 ──────────────────────────────────────────────────────
    [Fact]
    public async Task CreateDataKey_PersistsWrappedDek_NotPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        byte[] dek;
        using (var scope = _fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            dek = await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await UserDataKeys(db)
            .Where(k => k.JobSeekerId == seeker.Id)
            .ToListAsync(ct);

        rows.Count.ShouldBe(1, "första GetOrCreate ska skapa exakt en wrapped-DEK-rad");
        var row = rows[0];
        row.DekVersion.ShouldBe(1);
        row.CmkKeyId.ShouldNotBeNullOrWhiteSpace("cmk_key_id ska sättas vid create");
        row.WrappedDek.ShouldNotBeNull();
        row.WrappedDek.Length.ShouldBeGreaterThan(0);

        // Wrapped får ALDRIG vara lika med plaintext-DEK:en som store returnerade
        // — annars lagras en klartext-nyckel. Provider-agnostisk jämförelse
        // (fångar den faktiska returnerade DEK:en, oberoende av wrap-wire-format).
        row.WrappedDek.ShouldNotBe(dek);
    }

    // ── Scenario 5 ──────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrCreateDataKey_SameUser_ReusesExistingDek()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        byte[] first, second;
        using (var scope1 = _fixture.Services.CreateScope())
        {
            var store = scope1.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            first = await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }
        using (var scope2 = _fixture.Services.CreateScope())
        {
            var store = scope2.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            second = await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await UserDataKeys(db).Where(k => k.JobSeekerId == seeker.Id).ToListAsync(ct);

        rows.Count.ShouldBe(1, "andra anropet får INTE skapa en ny user_data_keys-rad");
        second.ShouldBe(first, "samma användare → samma unwrappade DEK");
    }

    // ── Scenario 6 ──────────────────────────────────────────────────────
    [Fact]
    public async Task GetOrCreateDataKey_DifferentUsers_IsolatedDeks()
    {
        var ct = TestContext.Current.CancellationToken;
        var seekerA = await SeedJobSeekerAsync(ct);
        var seekerB = await SeedJobSeekerAsync(ct);

        byte[] dekA, dekB;
        using (var scope = _fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            dekA = await store.GetOrCreateDataKeyAsync(seekerA.Id, ct);
            dekB = await store.GetOrCreateDataKeyAsync(seekerB.Id, ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rowA = await UserDataKeys(db).SingleAsync(k => k.JobSeekerId == seekerA.Id, ct);
        var rowB = await UserDataKeys(db).SingleAsync(k => k.JobSeekerId == seekerB.Id, ct);

        // Två distinkta wrapped-DEK-rader, ingen delning mellan användare.
        rowA.WrappedDek.ShouldNotBe(rowB.WrappedDek);
        dekA.ShouldNotBe(dekB, "två JobSeekers får aldrig dela DEK (ADR 0049 Beslut 1)");
    }

    // ── Scenario 7 ──────────────────────────────────────────────────────
    [Fact]
    public async Task DekCache_WithinScope_UnwrapsOncePerUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        // Skapa wrapped-raden i en första scope (DEK Generate, ej unwrap).
        using (var scope0 = _fixture.Services.CreateScope())
        {
            var store0 = scope0.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store0.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }

        // Nollställ unwrap-räknaren på den DELADE räknande provider-dekoratören
        // innan vi mäter memoiseringen (Worker-collection är seriell ⇒ deterministiskt).
        _fixture.Deks.ResetUnwrapCount();

        // Inom EN scope: flera GetOrCreate för samma user → DEK-unwrap ska ske
        // EN gång (cache memoiserar), inte per anrop.
        using (var scope = _fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();

            await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct);

            // Seam 3: konkreta cachens internal-räknare bekräftar memoisering.
            ConcreteCache(scope).UnwrapCountFor(seeker.Id).ShouldBe(
                1,
                "DEK-cachen ska unwrappa EN gång per user per scope (ej per anrop)");
        }

        // Provider-gränsräknaren ska bara ha sett ETT unwrap-anrop trots tre
        // GetOrCreate (cachen memoiserade de övriga) — det verkliga krypto-I/O:t.
        _fixture.Deks.UnwrapCount.ShouldBe(
            1,
            "DEK-unwrap (provider) ska ske exakt en gång per user per scope");
    }

    // ── Scenario 8 (C1-gate security Minor 2) ───────────────────────────
    [Fact]
    public async Task DekCache_OnScopeDispose_ZeroesPlaintextBuffers()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        ScopedUserDataKeyCache cache;
        using (var scope = _fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
            cache = ConcreteCache(scope);

            // Seam 3: internal peek bekräftar att DEK ligger cachad inom scope.
            cache.TryPeekCachedDek(seeker.Id, out var live).ShouldBeTrue(
                "DEK ska vara cachad inom scope:t");
            live.ToArray().ShouldNotBe(new byte[32], "cachad DEK är icke-noll inom scope");
        }
        // Scope (och därmed cachen) disposed här.

        // Security Minor 2: efter dispose ska alla cachade plaintext-DEK-
        // buffrar vara nollställda (CryptographicOperations.ZeroMemory) —
        // nyckelmaterial dör med scope:t (Seam 3 internal-flagga).
        cache.LastDisposedBuffersAllZeroed.ShouldBeTrue(
            "alla plaintext-DEK-buffrar ska ZeroMemory:as vid scope-dispose " +
            "(C1-gate security Minor 2, ADR 0049 — nyckelmaterial dör med scope:t)");
    }

    // ── Scenario 9 ──────────────────────────────────────────────────────
    [Fact]
    public async Task DekUnwrapFail_FailsClosed_NoCachedFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        // Skapa wrapped-raden först via grafen (Generate lyckas) så ResolveDek
        // tar unwrap-grenen i fail-scope:t.
        using (var seedScope = _fixture.Services.CreateScope())
        {
            var store = seedScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }

        // Seam 1 (architect: scenario 9 ensamt direkt-konstruerar store+cache+
        // failing-provider — fail-closed-vägen behöver ej DI-grafens äkthet, bara
        // att store kastar + cachen tom. Husets HardDeleteAccountsJob-
        // precedens). DbContext resolvas ur en scope så wrapped-raden finns.
        using var dbScope = _fixture.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ADR 0066 (#802): fail-closed-vägen mockar IDataKeyProvider direkt —
        // den tidigare KMS-klient-mocken är borta med providern.
        var failingProvider = Substitute.For<IDataKeyProvider>();
        failingProvider
            .UnwrapDataKeyAsync(
                Arg.Any<JobSeekerId>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns<Task<byte[]>>(_ =>
                throw new CryptographicException("Lokal DEK-unwrap nere"));

        using var failCache = new ScopedUserDataKeyCache();
        var inspector = dbScope.ServiceProvider.GetRequiredService<IDbExceptionInspector>();
        var failStore = new UserDataKeyStore(
            db, failingProvider, failCache, new FixedClock(DateTimeOffset.UtcNow), inspector);

        Exception? caught = null;
        byte[]? leaked = null;
        try
        {
            leaked = await failStore.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.ShouldNotBeNull("DEK unwrap-fel måste propageras (fail-closed)");
        leaked.ShouldBeNull("ingen klartext-DEK får returneras vid KMS-fel");
        failCache.TryPeekCachedDek(seeker.Id, out _).ShouldBeFalse(
            "cachen får INTE innehålla någon klartext/default-DEK efter KMS-fel");
    }

    // ── Scenario 10 (#501) — versionsblind läsväg: v2-rad kastar loud ────
    [Fact]
    public async Task ResolveDek_WhenHigherDekVersionExists_FailsClosed()
    {
        // #501 (Medium): en (owner, dek_version=2)-rad — vad naiv rotationskod
        // skulle infoga — får ALDRIG tyst returneras och dekryptera v1-ciphertext
        // med fel DEK (AES-GCM-tag-mismatch på varje läsning, hela datasetet
        // oläsbart). Single-version-guarden ska kasta loud i stället.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        // v1 skapas normalt via store:n.
        using (var scope = _fixture.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
        }

        // Simulera naiv rotation: infoga en (owner, dek_version=2)-rad direkt.
        using (var seedScope = _fixture.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<UserDataKey>().Add(new UserDataKey(
                seeker.Id,
                dekVersion: 2,
                wrappedDek: [0x4C, 0x01, 0x02, 0x03],
                cmkKeyId: "rotated-v2",
                createdAt: new FixedClock(DateTimeOffset.UtcNow).UtcNow));
            await db.SaveChangesAsync(ct);
        }

        // Fresh scope (tom cache) → ResolveDek väljer högsta versionen (v2) →
        // guarden kastar CryptographicException FÖRE unwrap; ingen tyst
        // fel-DEK-dekryptering.
        using var readScope = _fixture.Services.CreateScope();
        var readStore = readScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();

        byte[]? leaked = null;
        var ex = await Record.ExceptionAsync(async () =>
            leaked = await readStore.GetOrCreateDataKeyAsync(seeker.Id, ct));

        ex.ShouldNotBeNull(
            "en v2-rad utan versionsmedveten läsväg måste fail-closed:a (#501)");
        ex.ShouldBeOfType<CryptographicException>(
            "otvetydigt versionsfel → CryptographicException, ingen fel-DEK-dekrypt");
        leaked.ShouldBeNull("ingen DEK får returneras när versionen inte stöds");
    }

    // ── Scenario 11 (Low2) — first-use-race: samtidiga GetOrCreate ger EN rad ─
    [Fact]
    public async Task GetOrCreateDataKey_ConcurrentFirstUse_NoDuplicateNoThrow()
    {
        // Low2: två+ parallella requests för samma NYA ägare skapade båda en DEK
        // → PK-violation (job_seeker_id, dek_version) → förloraren 500:ade på
        // DbUpdateException. Get-or-create-upsert:en ska i stället fånga 23505,
        // re-query:a vinnaren och ge alla samma DEK — exakt EN rad, inget kast.
        // Mot riktig Postgres (Testcontainers): 8-vägs samtidighet träffar PK-
        // racen; invarianten (en rad, samma DEK, inget kast) håller oavsett om
        // race-grenen träffas, så testet är icke-flaky.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        const int concurrency = 8;
        var deks = await Task.WhenAll(
            Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
            {
                using var scope = _fixture.Services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
                return await store.GetOrCreateDataKeyAsync(seeker.Id, ct);
            }, ct)));

        // Inget kast: en ohanterad DbUpdateException hade propagerats av WhenAll.
        // Exakt EN wrapped-DEK-rad — ingen duplicerad first-use-insert.
        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await UserDataKeys(db)
            .Where(k => k.JobSeekerId == seeker.Id)
            .ToListAsync(ct);
        rows.Count.ShouldBe(1, "samtidig first-use får skapa exakt EN user_data_keys-rad");
        rows[0].DekVersion.ShouldBe(1);

        // Alla anropare fick vinnarens DEK — ingen fick en förlorar-DEK.
        foreach (var dek in deks)
        {
            dek.ShouldBe(deks[0], "alla samtidiga anropare ska få vinnarens DEK");
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
