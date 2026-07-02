using System.Data.Common;
using System.Security.Cryptography;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.Auditing;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 FAS 3.5 batch C3 — fält-kryptering på de tre TEXT-PII-kolumnerna via
/// interceptor-paret (ADR 0049 Beslut 4 + mekanik-not 1/3/4; CTO Approach A/B).
/// Mot riktig Postgres (Testcontainers via <see cref="WorkerTestFixture"/>) —
/// interceptor↔Npgsql-materialiserings-ordningen är load-bearing och INTE
/// normativt garanterad i Microsoft Learn ⇒ måste verifieras empiriskt
/// (CLAUDE.md/test-stack: InMemory förbjudet; ADR 0049 Mekanik-not 4).
///
/// <para>
/// <b>Berörda kolumner</b> (EncryptedFieldRegistry on-disk 2026-05-18):
/// <c>applications.cover_letter</c> (Application.CoverLetter),
/// <c>application_notes.content</c> (ApplicationNote.Content),
/// <c>follow_ups.note</c> (FollowUp.Note, nullable).
/// </para>
///
/// <para>
/// <b>Seam 1 (architect-domen 2026-05-18, Variant A):</b> KMS är ALLTID den
/// delade deterministiska <see cref="DeterministicFakeKms"/> som
/// <see cref="WorkerTestFixture"/> sista-vinner-registrerar för hela grafen —
/// ingen riktig AWS, produktkod orörd. Fail-closed-scenarierna (10/11)
/// direkt-konstruerar en failing-KMS-graf via <see cref="FailingKmsGraph"/>
/// (husets <c>UserDataKeyStoreIntegrationTests</c>-scenario-9-precedens).
/// </para>
///
/// <para>
/// <b>Write- OCH read-mekanik (CTO Approach A re-entrancy-fix 2026-05-18):</b>
/// efter Approach A är BÅDA interceptorerna rena synkrona cache-konsumenter.
/// <see cref="Jobbliggaren.Infrastructure.Security.FieldEncryptionSaveChangesInterceptor"/>
/// anropar ALDRIG längre <c>GetOrCreateDataKeyAsync</c>/KMS inifrån SaveChanges
/// (det orsakade re-entrant SaveChangesAsync → EF concurrency-deadlock,
/// ADR 0049 Mekanik-not 5). DEK värms istället av
/// <c>FieldEncryptionKeyPrefetchBehavior</c> i ett EGET pipeline-steg före
/// UnitOfWork — för BÅDE write och read. <see cref="WorkerTestFixture"/> kör
/// <see cref="WorkerSystemUser"/> (<c>UserId == null</c>) och går EJ via
/// Mediator-pipelinen, så testet simulerar prefetch i write-/läs-scopet exakt
/// som behaviorn gör (<see cref="PrefetchOwnerDekAsync"/>): resolva
/// <see cref="IUserDataKeyStore"/> + <see cref="ICurrentDataOwner"/>, anropa
/// <c>GetOrCreateDataKeyAsync(jobSeekerId)</c> + <c>SetOwner(jobSeekerId)</c>
/// FÖRE <c>SaveChangesAsync</c> (write) resp. materialiserings-läsningen (read).
/// Determinism/isolering &gt; pipeline-äkthet (samma kontrakt verifieras, ingen
/// flaky auth-graf). <b>Alla write-scenarier (1/3/4/5/7/8/10) kräver nu
/// prefetch-warm i write-scopet</b> — interceptorn skapar inte längre DEK.
/// </para>
///
/// <para>
/// <b>CTO #3 (iv) system-scope-passthrough (Mekanik-not 5b):</b>
/// <see cref="FieldDecryptionMaterializationInterceptor"/> är scope-
/// differentierad fail-closed. Krypterat fält men ingen cachad DEK:
/// (a) autentiserad ägar-scope (<c>ICurrentDataOwner.JobSeekerId is not null</c>)
/// ⇒ <see cref="CryptographicException"/> (felkonfig-användar-read får ALDRIG
/// tyst ciphertext); (b) system/Hangfire-scope (ingen owner satt — t.ex.
/// MarkGhosted/AccountHardDeleter) ⇒ lämna ciphertext orört, kasta INTE
/// (drift får ej krascha; konfidentialitet bevarad). Verifieras av
/// <c>SystemScope_NoOwnerContext_MaterializesEncrypted_LeavesCiphertext_NoThrow</c>
/// + regressionsskyddet <c>AuthenticatedScope_NoCachedDek_StillThrows</c>.
/// </para>
///
/// <para>
/// <b>Scenario 10 (KMS-fail-on-save):</b> efter Approach A inträffar KMS-felet
/// i prefetch-steget (<c>GetOrCreateDataKeyAsync</c> → GenerateDataKey kastar)
/// FÖRE <c>SaveChangesAsync</c> — ingen rad persisteras. Sekundärt: save UTAN
/// varm cache ⇒ SaveChangesInterceptorn kastar <see cref="CryptographicException"/>
/// (fail-closed), ingen klartext-rad.
/// </para>
///
/// <para>
/// <b>Scenario 11 (KMS-fail-on-read):</b> Approach B-konsekvens — vald
/// fail-closed-väg är "ingen cachad DEK ⇒ <see cref="CryptographicException"/>"
/// (autentiserad scope). I direkt-scope-testet simuleras prefetch INTE när KMS
/// är nere (KMS-felet inträffar i det simulerade prefetch-steget, fail-closed,
/// INNAN materialisering). Testet verifierar (1) att prefetch/GetOrCreateDataKeyAsync
/// kastar och (2) att materialisering UTAN cachad DEK men MED satt owner kastar
/// <see cref="CryptographicException"/> — ingen ciphertext returneras oläst.
/// </para>
///
/// <para>TDD-ordning (CLAUDE.md §2.4/§7): linjerad mot färdig C3-produktkod
/// on-disk 2026-05-18 (interceptor-paret + EncryptedFieldRegistry +
/// FieldEncryptionKeyPrefetchBehavior + ICurrentDataOwner). Specifikationstest
/// mot kontrakts-ytan, ej rad-för-rad bekräftelse av impl.</para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class FieldEncryptionInterceptorTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    // ── Seedning ────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "C3 Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static DomainApplication NewApplication(
        JobSeekerId owner, string? coverLetter) =>
        DomainApplication.Create(
            owner, jobAdId: null, coverLetter,
            manualPosting: null, new FixedClock(DateTimeOffset.UtcNow)).Value;

    /// <summary>
    /// Approach (b): simulerar <c>FieldEncryptionKeyPrefetchBehavior</c> i ett läs-
    /// scope. Värmer ägar-DEK i scopets <see cref="Jobbliggaren.Infrastructure.Security.ScopedUserDataKeyCache"/>
    /// + sätter <see cref="ICurrentDataOwner"/> exakt som behaviorn gör, så
    /// <see cref="FieldDecryptionMaterializationInterceptor"/> blir en ren
    /// synkron cache-hit. MÅSTE anropas i SAMMA scope som läsningen (cachen +
    /// owner är scoped).
    /// </summary>
    private static async Task PrefetchOwnerDekAsync(
        IServiceScope scope, JobSeekerId owner, CancellationToken ct)
    {
        var dataKeyStore = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore.GetOrCreateDataKeyAsync(owner, ct);
        CryptographicOperations.ZeroMemory(dek); // cachen äger sin egen kopia
    }

    // Rå kolumn-läsning förbi EF (kringgår dekrypt-interceptorn) — bevisar
    // on-disk-tillståndet, inte round-trippat värde.
    private static async Task<string?> RawScalarAsync(
        AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : (string)raw;
    }

    // ── 1. Cover letter persisteras som ciphertext, ej klartext ──────────
    [Fact]
    public async Task SaveApplication_CoverLetter_PersistsCiphertext_NotPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "Hemligt personligt brev — PERSONUPPGIFT-12345";

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            // Approach A: SaveChangesInterceptorn är ren cache-konsument →
            // prefetch FÖRE save i SAMMA scope (speglar
            // FieldEncryptionKeyPrefetchBehavior före UnitOfWork).
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        // rå DB-kolumn ska bära sentinel-prefix (ADR 0049 Beslut 4)
        rawColumn.ShouldStartWith("v1:");
        // Shouldly 4.3: ShouldNotContain(string, Case) — INGEN 3-arg
        // (string, Case, customMessage)-overload (gammal fil CS1503). Assert-
        // syftet bärs av v1:-prefixet ovan + namnet på testet.
        rawColumn.ShouldNotContain(secret, Case.Sensitive);
        rawColumn.ShouldNotContain("PERSONUPPGIFT", Case.Sensitive);
    }

    // ── 2. Round-trip — dekrypteras transparent vid läsning (Approach b) ─
    [Fact]
    public async Task ReadApplication_CoverLetter_DecryptsTransparently()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "Transparent round-trip cover letter";

        ApplicationId appId;
        using (var writeScope = _fixture.Services.CreateScope())
        {
            // Approach A: prefetch krävs nu även på write-vägen.
            await PrefetchOwnerDekAsync(writeScope, seeker.Id, ct);
            var db = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        // Approach (b): ny scope ⇒ ren ChangeTracker + tom DEK-cache. Simulera
        // prefetch-behaviorn FÖRE materialisering (annars fail-closed).
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loaded = await readDb.Applications
            .AsNoTracking()
            .SingleAsync(a => a.Id == appId, ct);

        loaded.CoverLetter.ShouldBe(secret,
            "läsning ska ge tillbaka klartext (decrypt-on-read materialisering)");
    }

    // ── 3. ApplicationNote — barn-entitet, ägare via ICurrentDataOwner ───
    [Fact]
    public async Task SaveApplicationNote_Content_PersistsCiphertext()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string note = "Privat anteckning om rekryteraren";

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            // Approach A: prefetch ägar-DEK i write-scopet (barn-ägare resolvas
            // av interceptorn via spårad parent — samma JobSeekerId-DEK).
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, coverLetter: null);
            app.AddNote(note, new FixedClock(DateTimeOffset.UtcNow))
                .IsSuccess.ShouldBeTrue();
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT content FROM application_notes WHERE application_id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        // barn-entitetens ägare (JobSeekerId) resolvas via spårad parent
        // Application på write (FieldEncryptionSaveChangesInterceptor.ResolveOwner)
        rawColumn.ShouldStartWith("v1:");
        rawColumn.ShouldNotContain(note, Case.Sensitive);

        // Round-trip: barn dekrypteras via ICurrentDataOwner (Approach b —
        // ApplicationNote saknar egen JobSeekerId, interceptorn läser owner).
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loaded = await readDb.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Notes)
            .SingleAsync(a => a.Id == appId, ct);
        loaded.Notes.ShouldHaveSingleItem().Content.ShouldBe(note);
    }

    // ── 4. FollowUp.Note — barn-entitet, samma ägar-resolution (write) ───
    [Fact]
    public async Task SaveFollowUp_Note_PersistsCiphertext()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string note = "Uppföljnings-anteckning med kontaktuppgifter";

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            // Approach A: prefetch ägar-DEK i write-scopet.
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, coverLetter: null);
            app.AddFollowUp(
                FollowUpChannel.Email,
                DateTimeOffset.UtcNow.AddDays(3),
                note,
                new FixedClock(DateTimeOffset.UtcNow)).IsSuccess.ShouldBeTrue();
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT note FROM follow_ups WHERE application_id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        rawColumn.ShouldStartWith("v1:");
        rawColumn.ShouldNotContain(note, Case.Sensitive);
    }

    // ── 5. FollowUp.Note null förblir null (nullable, ej krypterad) ──────
    [Fact]
    public async Task FollowUp_NullNote_StaysNull_NotEncrypted()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            // Approach A: prefetch i write-scopet (speglar prod-pipelinen även
            // när inget fält faktiskt krypteras — interceptorn skippar null).
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, coverLetter: null);
            app.AddFollowUp(
                FollowUpChannel.Phone,
                DateTimeOffset.UtcNow.AddDays(1),
                note: null,
                new FixedClock(DateTimeOffset.UtcNow)).IsSuccess.ShouldBeTrue();
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT note FROM follow_ups WHERE application_id = '{appId.Value}'",
            ct);

        rawColumn.ShouldBeNull(
            "null Note ska förbli NULL — aldrig krypteras till sentinel-tom-sträng " +
            "(SaveChangesInterceptor hoppar null/tom)");

        // Approach (b) — round-trip null förblir null (interceptorn rör ej null).
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loaded = await readDb.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.FollowUps)
            .SingleAsync(a => a.Id == appId, ct);
        loaded.FollowUps.ShouldHaveSingleItem().Note.ShouldBeNull();
    }

    // ── 6. Legacy klartext-rad läses utan fel (lazy-tolerans) ────────────
    [Fact]
    public async Task LegacyPlaintextRow_ReadsWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string legacyPlaintext = "PRE-MIGRERING klartext utan sentinel";

        // Simulera pre-C3-rad: rå INSERT förbi interceptorn (ingen v1:-prefix).
        var appId = ApplicationId.New();
        await RawInsertLegacyApplicationAsync(seeker.Id, appId, legacyPlaintext, ct);

        // Approach (b): legacy saknar sentinel ⇒ interceptorn lämnar orört
        // (IsEncrypted=false) — ingen DEK behövs. Prefetch ändå (matchar
        // produktionspipelinen för IRequiresFieldEncryptionKey-queryn) men cachen
        // träffas aldrig för denna kolumn (legacy-grenen returnerar tidigt).
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        DomainApplication? loaded = null;
        await Should.NotThrowAsync(async () =>
            loaded = await readDb.Applications
                .AsNoTracking()
                .SingleAsync(a => a.Id == appId, ct));

        loaded.ShouldNotBeNull();
        loaded.CoverLetter.ShouldBe(legacyPlaintext,
            "klartext-legacy (ingen sentinel) returneras orört — lazy-tolerans Beslut 4");
    }

    // ── 7. Legacy klartext lazy-migreras vid nästa write ─────────────────
    [Fact]
    public async Task LegacyPlaintextRow_LazyMigratesOnNextWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string legacyPlaintext = "Klartext som ska lazy-migreras";

        var appId = ApplicationId.New();
        await RawInsertLegacyApplicationAsync(seeker.Id, appId, legacyPlaintext, ct);

        // Modifiera + spara. SaveChangesInterceptorn ser ingen v1:-prefix ⇒
        // krypterar (lazy-migrering). Approach A: krypteringen kräver varm
        // ägar-DEK i write-scopet → prefetch FÖRE läsning+save. (Materialisering
        // av den gamla legacy-raden i sig kräver ingen DEK — saknar sentinel —
        // men encrypt-on-write gör det.)
        using (var writeScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(writeScope, seeker.Id, ct);
            var db = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = await db.Applications.SingleAsync(a => a.Id == appId, ct);
            app.TransitionTo(
                ApplicationStatus.Submitted, new FixedClock(DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        // klartext-rad ska vara krypterad efter nästa write (lazy-migrering Beslut 4)
        rawColumn.ShouldStartWith("v1:");
        rawColumn.ShouldNotContain(legacyPlaintext, Case.Sensitive);
    }

    // ── 8. Idempotent — redan krypterat värde dubbel-krypteras inte ──────
    [Fact]
    public async Task Idempotent_AlreadyEncryptedValue_NotDoubleEncrypted()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "Idempotens-brev";

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            // Approach A: första save krypterar → prefetch krävs.
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        // Andra save (status-ändring, CoverLetter orört). Interceptorn ska se
        // v1:-prefix (IsEncrypted=true) och hoppa — ej dubbel-kryptera. Prefetch
        // ändå (speglar prod-pipelinen; idempotent-skip kräver ingen DEK men
        // pipelinen kör behaviorn oavsett).
        using (var scope2 = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope2, seeker.Id, ct);
            var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = await db.Applications.SingleAsync(a => a.Id == appId, ct);
            app.TransitionTo(
                ApplicationStatus.Submitted, new FixedClock(DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        rawColumn.ShouldStartWith("v1:");
        // Exakt ETT sentinel-prefix — dubbel-kryptering skulle ge "v1:...v1:..."
        System.Text.RegularExpressions.Regex.Count(rawColumn, "v1:")
            .ShouldBe(1, "redan v1:-prefixat värde får INTE dubbel-krypteras");

        // Approach (b) round-trip: en enda dekrypt räcker ⇒ originalklartext.
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loaded = await readDb.Applications
            .AsNoTracking().SingleAsync(a => a.Id == appId, ct);
        loaded.CoverLetter.ShouldBe(secret,
            "en enda dekrypt ska räcka — bevisar att värdet ej dubbel-krypterades");
    }

    // ── 9. Cross-user — rätt per-användare-DEK, ingen förväxling ─────────
    [Fact]
    public async Task CrossUser_TwoApplications_DecryptWithCorrectPerUserDek()
    {
        var ct = TestContext.Current.CancellationToken;
        var seekerA = await SeedJobSeekerAsync(ct);
        var seekerB = await SeedJobSeekerAsync(ct);
        const string letterA = "Brev tillhörande användare A";
        const string letterB = "Brev tillhörande användare B";

        ApplicationId appA, appB;
        using (var scope = _fixture.Services.CreateScope())
        {
            // Värm BÅDA ägarnas DEK FÖRE entiteterna läggs till (speglar
            // FieldEncryptionKeyPrefetchBehavior före handler/UnitOfWork —
            // tom ChangeTracker när UserDataKeyStore persisterar DEK-raden).
            await PrefetchOwnerDekAsync(scope, seekerA.Id, ct);
            await PrefetchOwnerDekAsync(scope, seekerB.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = NewApplication(seekerA.Id, letterA);
            var b = NewApplication(seekerB.Id, letterB);
            appA = a.Id;
            appB = b.Id;
            db.Applications.AddRange(a, b);
            await db.SaveChangesAsync(ct);
        }

        // Approach (b): per-användare-DEK ⇒ varje läs-scope värmer ENBART sin
        // ägares DEK (precis som prefetch-behaviorn — owner härleds från
        // currentUser). Separata scopes bevisar att A:s DEK aldrig dekrypterar
        // B:s brev (ingen DEK-förväxling).
        string loadedA, loadedB;
        using (var scopeA = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scopeA, seekerA.Id, ct);
            var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
            loadedA = (await dbA.Applications.AsNoTracking()
                .SingleAsync(a => a.Id == appA, ct)).CoverLetter!;
        }
        using (var scopeB = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scopeB, seekerB.Id, ct);
            var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
            loadedB = (await dbB.Applications.AsNoTracking()
                .SingleAsync(a => a.Id == appB, ct)).CoverLetter!;
        }

        loadedA.ShouldBe(letterA, "användare A:s brev ska dekrypteras med A:s DEK");
        loadedB.ShouldBe(letterB,
            "användare B:s brev ska dekrypteras med B:s DEK — ingen DEK-förväxling");
    }

    // ── 10. KMS-fel vid save → fail-closed, ingen klartext persisterad ───
    [Fact]
    public async Task KmsFailOnSave_FailsClosed_NoPlaintextPersisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "Får ALDRIG nå disken som klartext";

        // Fail-closed-graf: speglar fixturen men sista-vinner-registrerar en
        // KMS som kastar vid GenerateDataKey. Approach A: SaveChangesInterceptorn
        // anropar INTE längre KMS — felet inträffar i prefetch-steget
        // (FieldEncryptionKeyPrefetchBehavior → GetOrCreateDataKeyAsync →
        // GenerateDataKey) FÖRE save. Sekundärt: en save UTAN varm cache →
        // SaveChangesInterceptorn kastar CryptographicException (fail-closed,
        // CTO lucka 5). Ingendera vägen persisterar klartext.
        var failingKms = Substitute.For<IAmazonKeyManagementService>();
        failingKms
            .GenerateDataKeyAsync(
                Arg.Any<GenerateDataKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GenerateDataKeyResponse>>(_ =>
                throw new AmazonKeyManagementServiceException(
                    "KMS GenerateDataKey nere"));

        await using var failGraph =
            FailingKmsGraph.Build(_fixture.ConnectionString, failingKms);

        var appId = ApplicationId.New();
        using (var scope = failGraph.Provider.CreateScope())
        {
            // Primärt: prefetch-steget (= FieldEncryptionKeyPrefetchBehavior,
            // före UnitOfWork) kastar — KMS GenerateDataKey nere. Inget save
            // körs alls ⇒ ingen rad.
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            var owner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
            owner.SetOwner(seeker.Id);

            var prefetchEx = await Record.ExceptionAsync(async () =>
                await store.GetOrCreateDataKeyAsync(seeker.Id, ct));
            prefetchEx.ShouldNotBeNull(
                "KMS GenerateDataKey-fel måste propageras i prefetch-steget " +
                "(fail-closed FÖRE save — Approach A: interceptorn anropar ej KMS)");

            // Sekundärt: även om save ändå körs UTAN varm cache (prefetch
            // kastade ⇒ ingen cachad DEK) kastar SaveChangesInterceptorn
            // CryptographicException — ingen klartext-DML.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);

            var saveEx = await Record.ExceptionAsync(async () =>
                await db.SaveChangesAsync(ct));
            saveEx.ShouldNotBeNull();
            saveEx.ShouldBeOfType<CryptographicException>(
                "save utan varm DEK-cache måste fail-closed:a i " +
                "FieldEncryptionSaveChangesInterceptor (ingen cachad DEK ⇒ kasta " +
                "FÖRE DML, hela SaveChanges rullas)");
        }

        // Ingen rad får ha persisterats (allra minst inte som klartext).
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var raw = await RawScalarAsync(
            verifyDb,
            $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
            ct);
        raw.ShouldBeNull(
            "KMS-fel vid save måste fail-closed:a — ingen klartext-rad får persisteras");
    }

    // ── 11. KMS-fel vid read → fail-closed (Approach B: prefetch kastar) ─
    [Fact]
    public async Task KmsFailOnRead_FailsClosed_NoPlaintextReturned()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "Krypterad rad som inte får läcka vid KMS-fel";

        // Skapa en korrekt krypterad rad via den delade fake-KMS:en.
        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        var failingKms = Substitute.For<IAmazonKeyManagementService>();
        failingKms
            .DecryptAsync(Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DecryptResponse>>(_ =>
                throw new AmazonKeyManagementServiceException("KMS Decrypt nere"));

        await using var failGraph =
            FailingKmsGraph.Build(_fixture.ConnectionString, failingKms);

        using var readScope = failGraph.Provider.CreateScope();

        // Approach B fail-closed-väg 1: det simulerade prefetch-steget
        // (= FieldEncryptionKeyPrefetchBehavior) anropar GetOrCreateDataKeyAsync →
        // KMS Decrypt nere ⇒ kastar FÖRE materialisering. Ciphertext når
        // aldrig läsvägen.
        var store = readScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var owner = readScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        owner.SetOwner(seeker.Id);

        var prefetchEx = await Record.ExceptionAsync(async () =>
            await store.GetOrCreateDataKeyAsync(seeker.Id, ct));
        prefetchEx.ShouldNotBeNull(
            "KMS Decrypt-fel i prefetch-steget måste propageras (fail-closed " +
            "INNAN materialisering — Approach B)");

        // Approach B fail-closed-väg 2: även om materialisering ändå körs UTAN
        // varm cache (prefetch kastade ⇒ ingen cachad DEK) kastar
        // FieldDecryptionMaterializationInterceptor CryptographicException —
        // ingen ciphertext/null-fallback returneras oläst.
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        DomainApplication? loaded = null;
        var materializeEx = await Record.ExceptionAsync(async () =>
            loaded = await readDb.Applications
                .AsNoTracking()
                .SingleAsync(a => a.Id == appId, ct));

        materializeEx.ShouldNotBeNull(
            "krypterat värde utan cachad DEK måste kasta vid materialisering");
        materializeEx.ShouldBeOfType<CryptographicException>(
            "fail-closed-väg: FieldDecryptionMaterializationInterceptor kastar " +
            "CryptographicException när ägar-DEK saknas i scope-cachen");
        loaded.ShouldBeNull(
            "ingen klartext eller null-fallback får returneras vid dekrypt-KMS-fel");
    }

    // ── 12. System/Hangfire-scope: ingen owner ⇒ ciphertext orört, ej kast ─
    [Fact]
    public async Task SystemScope_NoOwnerContext_MaterializesEncrypted_LeavesCiphertext_NoThrow()
    {
        // CTO #3 (iv) 2026-05-18 / ADR 0049 Mekanik-not 5b: ett system-jobb
        // (MarkGhosted/AccountHardDeleter-mönstret) materialiserar en krypterad
        // Application UTAN att ICurrentDataOwner är satt och UTAN prefetch.
        // Förväntat: ingen exception (drift får ej krascha), CoverLetter förblir
        // ciphertext (konfidentialitet bevarad — exponeras aldrig som plaintext),
        // och en efterföljande SaveChanges är idempotent (encrypt-interceptorn
        // skippar redan-v1:-prefixat värde, kräver ingen DEK).
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "System-scope brev — ska förbli ciphertext";

        ApplicationId appId;
        using (var writeScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(writeScope, seeker.Id, ct);
            var db = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        // Ny scope: INGEN PrefetchOwnerDekAsync ⇒ tom DEK-cache OCH
        // ICurrentDataOwner.JobSeekerId == null (system-scope, simulerar
        // MarkGhosted/Hangfire-jobb som aldrig sätter owner).
        using var systemScope = _fixture.Services.CreateScope();
        var currentDataOwner =
            systemScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.JobSeekerId.ShouldBeNull(
            "system-scope: ingen owner satt (vakt mot felaktig fixtur-default)");

        var systemDb = systemScope.ServiceProvider.GetRequiredService<AppDbContext>();

        DomainApplication? loaded = null;
        await Should.NotThrowAsync(async () =>
            loaded = await systemDb.Applications
                .FirstOrDefaultAsync(a => a.Id == appId, ct));

        loaded.ShouldNotBeNull();
        loaded.CoverLetter.ShouldNotBeNull();
        loaded.CoverLetter.ShouldStartWith("v1:");

        // Idempotent re-save (mutera annan kolumn): encrypt-interceptorn ser
        // v1:-prefix ⇒ skippar, ingen DEK krävs, ciphertext oförändrad.
        loaded.TransitionTo(
            ApplicationStatus.Submitted, new FixedClock(DateTimeOffset.UtcNow));
        await Should.NotThrowAsync(async () => await systemDb.SaveChangesAsync(ct));

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
            ct);
        rawColumn.ShouldNotBeNull();
        rawColumn.ShouldStartWith("v1:");
        rawColumn.ShouldNotContain(secret, Case.Sensitive);
        System.Text.RegularExpressions.Regex.Count(rawColumn, "v1:")
            .ShouldBe(1,
                "system-scope re-save får INTE dubbel-kryptera eller dekryptera " +
                "ciphertext — idempotent skip (CTO #3 (iv))");
    }

    // ── 13. Autentiserad scope utan cachad DEK ⇒ kastar (regressionsskydd) ─
    [Fact]
    public async Task AuthenticatedScope_NoCachedDek_StillThrows()
    {
        // Regressionsskydd: CTO #3 (iv) luckrar INTE upp användar-vägen. En
        // autentiserad scope (ICurrentDataOwner satt) men UTAN varm DEK-cache
        // (ingen GetOrCreateDataKeyAsync) ska fortfarande fail-closed:a med
        // CryptographicException vid materialisering av krypterat fält.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string secret = "Användar-scope brev — fail-closed utan cachad DEK";

        ApplicationId appId;
        using (var writeScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(writeScope, seeker.Id, ct);
            var db = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, secret);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        // Ny scope: sätt owner (autentiserad) men kör INTE prefetch
        // (GetOrCreateDataKeyAsync) ⇒ ingen cachad DEK. Detta är exakt den
        // felkonfig-väg #3 (iv) avgränsar mot system-scopet.
        using var readScope = _fixture.Services.CreateScope();
        var owner = readScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        owner.SetOwner(seeker.Id);
        owner.JobSeekerId.ShouldNotBeNull(
            "förutsättning: autentiserad scope (owner satt) men ingen varm DEK");

        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        DomainApplication? loaded = null;
        var ex = await Record.ExceptionAsync(async () =>
            loaded = await readDb.Applications
                .AsNoTracking()
                .SingleAsync(a => a.Id == appId, ct));

        ex.ShouldNotBeNull();
        ex.ShouldBeOfType<CryptographicException>(
            "autentiserad ägar-scope utan cachad DEK måste fail-closed:a — " +
            "CTO #3 (iv) får ALDRIG ge tyst ciphertext på användar-vägen");
        loaded.ShouldBeNull(
            "ingen klartext eller ciphertext-fallback får returneras");
    }

    // ── 14. #500 — användarklartext som liknar sentinel krypteras (proveniens) ─
    [Fact]
    public async Task SaveApplicationNote_UserTextLooksLikeSentinel_PersistsCiphertext_RoundTrips()
    {
        // #500 (High): innehållsgaten (IsEncrypted(plaintext), regex ^v\d+:)
        // tolkade användarklartext som börjar med sentinel-mönstret som "redan
        // krypterad" → skippade kryptering → klartext-PII at-rest + permanent
        // 500 på läsvägen (Decrypt fail-closar). Proveniensgaten (Added /
        // IsModified, inte innehåll) krypterar den nu. Exakt fynd-repro: en
        // anteckning vars text kolliderar med sentinel-prefixet.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string note = "v1: ringde rekryteraren om tjänsten och fick svar";

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, coverLetter: null);
            app.AddNote(note, new FixedClock(DateTimeOffset.UtcNow))
                .IsSuccess.ShouldBeTrue();
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT content FROM application_notes WHERE application_id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        rawColumn.ShouldStartWith("v1:");
        // Bevis att den KRYPTERADES, ej lagrades rå: distinkt PII-fragment får
        // inte finnas i on-disk-värdet (den buggiga koden lagrade klartext).
        rawColumn.ShouldNotContain("ringde rekryteraren", Case.Sensitive);
        // Exakt ETT sentinel-prefix (base64-alfabetet saknar kolon) — ingen
        // dubbel-prefixning.
        System.Text.RegularExpressions.Regex.Count(rawColumn, "v1:")
            .ShouldBe(1);

        // Rundresa: den sentinel-kolliderande klartexten ska överleva exakt
        // (bevisar att den krypterades EN gång, ej dubbel-krypterades).
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loaded = await readDb.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Notes)
            .SingleAsync(a => a.Id == appId, ct);
        loaded.Notes.ShouldHaveSingleItem().Content.ShouldBe(note,
            "sentinel-kolliderande klartext måste round-trippa exakt (#500)");
    }

    // ── 15. #500 — cover letter med "v2:"-liknande klartext (regex-bredd) ─
    [Fact]
    public async Task SaveApplication_CoverLetterLooksLikeSentinel_PersistsCiphertext_RoundTrips()
    {
        // Sentinel-regexen är ^v\d+: (alla versioner), inte bara literal "v1:".
        // En cover letter som börjar med "v2:" måste också krypteras — inte
        // felklassas som ciphertext.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        const string coverLetter = "v2: min ansökan börjar med en versionsliknande rad";

        ApplicationId appId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = NewApplication(seeker.Id, coverLetter);
            appId = app.Id;
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rawColumn = await RawScalarAsync(
            verifyDb,
            $"SELECT cover_letter FROM applications WHERE id = '{appId.Value}'",
            ct);

        rawColumn.ShouldNotBeNull();
        // Krypterad med v1-layouten (vår Encrypt emitterar alltid v1:).
        rawColumn.ShouldStartWith("v1:");
        rawColumn.ShouldNotContain("min ansökan", Case.Sensitive);
        rawColumn.ShouldNotBe(coverLetter);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loaded = await readDb.Applications
            .AsNoTracking()
            .SingleAsync(a => a.Id == appId, ct);
        loaded.CoverLetter.ShouldBe(coverLetter,
            "\"v2:\"-liknande klartext ska round-trippa exakt (#500 regex-bredd)");
    }

    // ── Hjälpare ────────────────────────────────────────────────────────

    /// <summary>
    /// Rå INSERT förbi SaveChangesInterceptorn (ingen v1:-prefix) — simulerar
    /// en pre-C3-klartext-rad. Kolumn-listan matchar ApplicationConfiguration
    /// (snake_case via UseSnakeCaseNamingConvention; Status lagras som
    /// string-namn; ghosted_threshold_days NOT NULL default 21; deleted_at
    /// NULL ⇒ passerar global query-filter).
    /// </summary>
    private async Task RawInsertLegacyApplicationAsync(
        JobSeekerId jobSeekerId,
        ApplicationId appId,
        string legacyPlaintext,
        CancellationToken ct)
    {
        using var seedScope = _fixture.Services.CreateScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO applications
                (id, job_seeker_id, cover_letter, status,
                 created_at, updated_at, last_status_change_at,
                 ghosted_threshold_days)
            VALUES
                (@id, @js, @cl, 'Draft',
                 now(), now(), now(), 21)
            """;
        AddParam(cmd, "@id", appId.Value);
        AddParam(cmd, "@js", jobSeekerId.Value);
        AddParam(cmd, "@cl", legacyPlaintext);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Speglar <see cref="WorkerTestFixture"/>:s DI-graf men sista-vinner-
    /// registrerar en valfri (typiskt failing) KMS-klient. Använder ENDAST
    /// publika produktionsregistreringar (<c>AddPersistence</c> registrerar
    /// C3-interceptor-paret + DbContext + KMS-graf via AddInterceptors) —
    /// ingen invented harness, inga test-only produkt-typer. Samma migrations-
    /// DB (delad container via <c>WorkerTestFixture.ConnectionString</c>) så
    /// rader skrivna här syns i fixturens verify-scope.
    /// </summary>
    private sealed class FailingKmsGraph : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }

        public static FailingKmsGraph Build(
            string connectionString, IAmazonKeyManagementService kms)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = connectionString,
                    ["FieldEncryption:CmkKeyId"] =
                        "arn:aws:kms:eu-north-1:000000000000:key/td13-test-cmk",
                    ["FieldEncryption:AwsRegion"] = "eu-north-1",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();

            services.AddPersistence(configuration);
            services.AddSingleton<ICurrentUser, WorkerSystemUser>();
            services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
            services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();

            // Sista-vinner: hela grafen (interceptor-paret → IUserDataKeyStore →
            // KmsDataKeyProvider) kör den (failing) KMS:en. Samma Seam-1-mönster
            // som WorkerTestFixture.
            services.AddSingleton(kms);

            services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
                new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
                {
                    EnvironmentName = "Test",
                    ApplicationName = "Jobbliggaren.Worker.IntegrationTests",
                    ContentRootPath = AppContext.BaseDirectory,
                });

            return new FailingKmsGraph
            {
                Provider = services.BuildServiceProvider(),
            };
        }

        public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
