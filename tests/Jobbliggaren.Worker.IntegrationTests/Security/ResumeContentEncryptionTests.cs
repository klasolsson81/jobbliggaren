using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Persistence.Migrations;
using Jobbliggaren.Worker.Auditing;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 FAS 3.5 batch <b>C4.4 — integration-svit för den verkliga #1c-mekaniken</b>
/// (ADR 0049 Mekanik-not 6 Form B) på <c>resume_versions.content_enc</c>: domän-VO
/// <see cref="ResumeVersion.Content"/> är EF-<c>Ignore</c>:ad, JSON-serialiseras
/// → krypteras → skrivs till krypterad text-shadow <c>content_enc</c> (enda
/// källan post-cutover #507a; legacy <c>content</c>-plaintext-fallbacken
/// retirerad, se HISTORICAL-noten nedan). Mot riktig Postgres (Testcontainers
/// via <see cref="WorkerTestFixture"/>) — exakt
/// samma stack/disciplin som C3-sviten
/// (<see cref="FieldEncryptionInterceptorTests"/>): InMemory förbjudet
/// (CLAUDE.md/test-stack; ADR 0049 Mekanik-not 4), interceptor↔Npgsql-
/// materialiserings-ordningen är load-bearing och måste verifieras empiriskt.
///
/// <para>
/// <b>Write-/read-mekanik (CTO Approach A/B, ärvd från C3):</b> båda
/// interceptorerna är rena synkrona cache-konsumenter — DEK värms av
/// <c>FieldEncryptionKeyPrefetchBehavior</c> i ett eget pipeline-steg före
/// UnitOfWork (write OCH read). <see cref="WorkerTestFixture"/> kör
/// <see cref="WorkerSystemUser"/> och går EJ via Mediator-pipelinen, så testet
/// simulerar prefetch i write-/läs-scopet exakt som behaviorn gör
/// (<see cref="PrefetchOwnerDekAsync"/>). ResumeVersion saknar egen
/// JobSeekerId — ägaren resolvas via spårad <see cref="Resume"/> i
/// ChangeTracker (<c>Resume.JobSeekerId</c>); ägar-DEK:n är JobSeekerns.
/// </para>
///
/// <para>
/// <b>Backfill-fallback RETIRED post-cutover (#507a / ADR 0049 Beslut 5 steg 3;
/// the description below is HISTORICAL: content_enc is now the sole source,
/// EncryptedFieldRegistry LegacyShadowProperty = null, and a content_enc IS NULL
/// row materializes Content as null in every scope, see scenario 3):</b>
/// <c>content_enc</c> null/icke-sentinel ⇒ läs legacy klartext-JSON ur
/// <c>content</c>-jsonb (rå-shadow <c>ContentLegacyJson</c>, ingen decrypt,
/// ingen DEK, ALLA scopes inkl. system). Sentinel + ägar-scope + cachad DEK ⇒
/// decrypt → <c>FromJson</c>. Autentiserad scope + ingen DEK på krypterad rad
/// ⇒ <see cref="CryptographicException"/> (CTO #3 (iv) ej uppluckrad för
/// Resume-vägen); system-scope (owner null) ⇒ passthrough, Content stays null.
/// </para>
///
/// <para>
/// <b>C4.2a-gaten subsumerad:</b> den separata
/// <c>ResumeContentEncShadowReadGateTests</c> var en engångs-pre-implementation-
/// GO-gate (returnerade GRÖN före impl och avblockerade C4.2:
/// <c>GetPropertyValue&lt;string&gt;("ContentEnc")</c> under
/// <c>AsNoTracking</c> utan ChangeTracker-entry). Dess invariant är nu
/// empiriskt utövad av den VERKLIGA produktions-read-interceptorn i scenario 1
/// (round-trip-läsning under <c>.AsNoTracking()</c>) — gaten är därför
/// raderad. <b>C4.0-proben</b> (<c>ResumeContentMaterializationProbeTests</c>)
/// subsumerades på SAMMA grund: #1c eliminerade JSON-ValueConverter:n
/// (<c>builder.Ignore(rv =&gt; rv.Content)</c>) → probens load-bearing premiss
/// (prod-modellen applicerar JSON-VC) föll, dess VC↔interceptor-ordnings-
/// invariant är logiskt tom (ingen VC kvar att regressera mot), och #1c:s
/// faktiska read-ordnings-invariant (shadow-<c>GetPropertyValue</c> under
/// <c>AsNoTracking</c>) utövas av scenario 1 genom den verkliga prod-
/// interceptorn. Proben är därför raderad (senior-cto-advisor-triage
/// 2026-05-19, ADR 0049 Mekanik-not 6-reconciliation, STOPP V-flaggad).
/// </para>
///
/// <para>
/// <b>Deep equality (scenario 1):</b> <see cref="ResumeContent"/> har
/// reference-baserad collection-equality (record, se dess xml-doc) ⇒ original
/// och inläst jämförs INTE med <c>Equals</c> utan fält-för-fält samt via en
/// kanonisk re-serialisering med samma <see cref="JsonSerializerOptions"/> som
/// produktionsmekaniken (camelCase, ej indenterad — speglar
/// EncryptedFieldRegistry.ContentJsonOptions; SPOT-paritet verifieras genom att
/// round-trippen faktiskt lyckas).
/// </para>
///
/// <para>TDD-ordning (CLAUDE.md §2.4/§7): linjerad mot färdig C4.2-produktkod
/// on-disk 2026-05-19 (ResumeVersionConfiguration #1c + EncryptedFieldRegistry
/// Form B + interceptor-paret). Specifikationstest mot kontrakts-ytan, ej
/// rad-för-rad bekräftelse av impl.</para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ResumeContentEncryptionTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    /// <summary>Kanonisk JSON-policy — speglar EncryptedFieldRegistry.ContentJsonOptions (SPOT).</summary>
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    // ── Seedning ────────────────────────────────────────────────────────

    private async Task<JobSeeker> SeedJobSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = JobSeeker.Register(
            Guid.NewGuid(), "C4.4 Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private static ResumeContent RichContent(string fullNameMarker) =>
        new(
            new PersonalInfo(
                fullNameMarker, "anna@example.com", "070-1234567", "Stockholm"),
            experiences:
            [
                new Experience(
                    "Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 6, 30),
                    "Byggde betaltjänster i .NET."),
                new Experience(
                    "Globex AB", "Senior-utvecklare",
                    new DateOnly(2024, 7, 1), null,
                    "Plattformsarkitektur."),
            ],
            educations:
            [
                new Education(
                    "KTH", "Civilingenjör Datateknik",
                    new DateOnly(2016, 8, 20), new DateOnly(2021, 6, 10)),
            ],
            skills:
            [
                new Skill("C#", 5),
                new Skill("PostgreSQL", 4),
            ],
            summary: "Erfaren backend-utvecklare med fokus på betaltjänster.");

    /// <summary>
    /// Seedar JobSeeker + Resume (Master-version) och ersätter Master-innehållet
    /// med <paramref name="content"/>. Kräver prefetch i write-scopet (Form B
    /// encrypt-on-write — interceptorn skapar inte längre DEK). Returnerar
    /// (ResumeId, MasterVersionId, JobSeekerId).
    /// </summary>
    private async Task<(ResumeId ResumeId, ResumeVersionId VersionId, JobSeekerId Owner)>
        SeedEncryptedMasterAsync(ResumeContent content, CancellationToken ct)
    {
        var seeker = await SeedJobSeekerAsync(ct);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        ResumeId resumeId;
        ResumeVersionId versionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scope, seeker.Id, ct);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var resume = Resume.Create(
                seeker.Id, "C4.4-CV", "Anna Andersson", clock).Value;
            resume.UpdateMasterContent(content, clock).IsSuccess.ShouldBeTrue();
            resumeId = resume.Id;
            versionId = resume.MasterVersion.Id;
            db.Resumes.Add(resume);
            await db.SaveChangesAsync(ct);
        }

        return (resumeId, versionId, seeker.Id);
    }

    /// <summary>
    /// Simulerar <c>FieldEncryptionKeyPrefetchBehavior</c> i scopet: värmer
    /// ägar-DEK i scopets cache + sätter <see cref="ICurrentDataOwner"/> exakt
    /// som behaviorn gör. MÅSTE anropas i SAMMA scope som write/read (cache +
    /// owner är scoped).
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

    // Rå kolumn-läsning förbi EF (kringgår dekrypt-interceptorn) — bevisar
    // on-disk-tillståndet, ej round-trippat värde.
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

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>
    /// Asserterar att två <see cref="ResumeContent"/> är lika PER VÄRDE
    /// (ResumeContent har reference-baserad collection-equality ⇒ Equals
    /// duger ej). Fält-för-fält + kanonisk JSON-jämförelse.
    /// </summary>
    private static void ShouldDeepEqual(ResumeContent actual, ResumeContent expected)
    {
        actual.PersonalInfo.ShouldBe(expected.PersonalInfo); // record value-equality (inga collections)
        actual.Summary.ShouldBe(expected.Summary);

        actual.Experiences.Count.ShouldBe(expected.Experiences.Count);
        for (var i = 0; i < expected.Experiences.Count; i++)
            actual.Experiences[i].ShouldBe(expected.Experiences[i]);

        actual.Educations.Count.ShouldBe(expected.Educations.Count);
        for (var i = 0; i < expected.Educations.Count; i++)
            actual.Educations[i].ShouldBe(expected.Educations[i]);

        actual.Skills.Count.ShouldBe(expected.Skills.Count);
        for (var i = 0; i < expected.Skills.Count; i++)
            actual.Skills[i].ShouldBe(expected.Skills[i]);

        // Kanonisk re-serialisering (samma policy som produktionsmekaniken) —
        // fångar struktur-drift som fält-loopen inte täcker.
        JsonSerializer.Serialize(actual, CanonicalJson)
            .ShouldBe(JsonSerializer.Serialize(expected, CanonicalJson));
    }

    // ── 1. Round-trip deep equality ─────────────────────────────────────
    [Fact]
    public async Task RoundTrip_RichResumeContent_DeepEqualsByValue_FreshScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = RichContent("Anna Andersson RT-1");

        var (resumeId, _, owner) = await SeedEncryptedMasterAsync(original, ct);

        // Ny scope ⇒ ren ChangeTracker + tom DEK-cache. Prefetch FÖRE
        // materialisering (annars fail-closed). AsNoTracking ⇒ ingen
        // ChangeTracker-entry — exakt den väg C4.2a-gaten verifierade, nu mot
        // den VERKLIGA produktions-read-interceptorn.
        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, owner, ct);
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var resume = await readDb.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .SingleAsync(r => r.Id == resumeId, ct);

        var loaded = resume.MasterVersion.Content;
        loaded.ShouldNotBeNull(
            "Content måste materialiseras via #1c-read-interceptorn (decrypt → FromJson)");
        ShouldDeepEqual(loaded, original);
    }

    // ── 2. Ciphertext on disk, legacy content NULL ──────────────────────
    [Fact]
    public async Task Ciphertext_ContentEncSentinel_LegacyContentNull_OnDisk()
    {
        var ct = TestContext.Current.CancellationToken;
        const string piiMarker = "PERSONUPPGIFT-CIPHERTEXT-9911";
        var original = RichContent(piiMarker);

        var (_, versionId, _) = await SeedEncryptedMasterAsync(original, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contentEnc = await RawScalarAsync(
            verifyDb,
            $"SELECT content_enc FROM resume_versions WHERE id = '{versionId.Value}'",
            ct);
        contentEnc.ShouldNotBeNull();
        contentEnc.ShouldStartWith("v1:");
        contentEnc.ShouldNotContain(piiMarker, Case.Sensitive);

        var legacyContent = await RawScalarAsync(
            verifyDb,
            $"SELECT content FROM resume_versions WHERE id = '{versionId.Value}'",
            ct);
        legacyContent.ShouldBeNull(
            "EF skriver ALDRIG legacy `content` för content_enc-only-rader " +
            "(ResumeVersionConfiguration: ContentLegacyJson PropertySaveBehavior.Ignore)");
    }

    // ── 3. Post-cutover: legacy plaintext fallback retired (#507a) ───────────────────
    // #507a / ADR 0049 Beslut 5 steg 3. Before the cutover a content_enc-null row
    // with legacy `content` jsonb materialized Content via the fallback. After the
    // mapping flip (EncryptedFieldRegistry LegacyShadowProperty = null) the fallback
    // is gone: such a row materializes Content == null in EVERY scope and never
    // throws (content_enc is null, so the encrypted-branch fail-closed is never
    // reached). The fitness gate + null-out migration guarantee no such row exists
    // in prod; this test locks the flip.
    [Fact]
    public async Task PostCutover_LegacyOnlyRow_FallbackRetired_MaterializesNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var legacy = RichContent("Legacy Jsonb Person (post-cutover)");
        var legacyJson = JsonSerializer.Serialize(legacy, CanonicalJson);

        var (resumeId, versionId) =
            await RawInsertLegacyResumeAsync(seeker.Id, legacyJson, ct);

        // (a) Authenticated scope WITH prefetch: content_enc is null so no decrypt,
        // and the legacy fallback is retired, so Content stays null and no throw.
        using (var withScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(withScope, seeker.Id, ct);
            var db = withScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Resume? loaded = null;
            await Should.NotThrowAsync(async () =>
                loaded = await db.Resumes
                    .AsNoTracking().Include(r => r.Versions)
                    .SingleAsync(r => r.Id == resumeId, ct));
            loaded.ShouldNotBeNull();
            loaded.Versions.ShouldHaveSingleItem().Content.ShouldBeNull(
                "post-cutover: a content_enc IS NULL row no longer falls back to " +
                "legacy plaintext (LegacyShadowProperty = null)");
        }

        // (b) System-scope WITHOUT prefetch/owner: same outcome, no fallback, no
        // throw, Content null. (Pre-cutover this materialized the legacy plaintext.)
        using (var sysScope = _fixture.Services.CreateScope())
        {
            sysScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>()
                .JobSeekerId.ShouldBeNull(
                    "system-scope: no owner set (guard against fixture default)");
            var db = sysScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Resume? loaded = null;
            await Should.NotThrowAsync(async () =>
                loaded = await db.Resumes
                    .AsNoTracking().Include(r => r.Versions)
                    .SingleAsync(r => r.Id == resumeId, ct));
            loaded.ShouldNotBeNull();
            loaded.Versions.ShouldHaveSingleItem().Content.ShouldBeNull();
        }

        // The legacy plaintext still sits on disk (physical DROP deferred, Beslut 5
        // steg 4) but is no longer reachable through the app.
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await RawScalarAsync(
                verifyDb,
                $"SELECT content FROM resume_versions WHERE id = '{versionId.Value}'",
                ct))
            .ShouldNotBeNull(
                "the raw-inserted legacy row keeps its plaintext on disk until the " +
                "deferred physical DROP; it is just unreachable now");
    }

    // ── 4. Cutover migration: embedded fail-loud guard (#507a) ─────────────────────────
    // Seeds one legacy-only row and runs the exact guard SQL (SPOT constant);
    // expects raise_exception (P0001) so the cutover cannot silently orphan CVs.
    [Fact]
    public async Task CutoverGuard_LegacyOnlyRowPresent_RaisesAndAborts()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var legacyJson = JsonSerializer.Serialize(
            RichContent("Guard trips on me"), CanonicalJson);
        var (resumeId, _) =
            await RawInsertLegacyResumeAsync(seeker.Id, legacyJson, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ex = await Record.ExceptionAsync(async () =>
                await db.Database.ExecuteSqlRawAsync(
                    NullResumeVersionLegacyContent.PreconditionGuardSql, ct));

            ex.ShouldNotBeNull(
                "the guard must abort when a legacy-only row (content_enc IS NULL " +
                "AND content IS NOT NULL) remains");
            var pg = ex.ShouldBeOfType<PostgresException>();
            pg.SqlState.ShouldBe("P0001",
                "PL/pgSQL RAISE EXCEPTION maps to SQLSTATE raise_exception");
            pg.MessageText.ShouldContain("plaintext cutover precondition failed");
        }

        // Cleanup: this legacy-only row would otherwise persist in the shared
        // [Collection("Worker")] DB and could trip a later global fitness assertion.
        using (var cleanupScope = _fixture.Services.CreateScope())
        {
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            // ExecuteSqlAsync parameterizes the interpolation hole (EF1002-safe).
            await cleanupDb.Database.ExecuteSqlAsync(
                $"DELETE FROM resumes WHERE id = {resumeId.Value}", ct);
        }
    }

    // ── 4b. Cutover migration: null-out UPDATE effect ───────────────────
    // #507a. The null-out clears legacy `content` for every migrated (content_enc)
    // row while content_enc still round-trips. Seeds a real ciphertext row, force-
    // writes dual-state legacy `content`, runs the exact null-out SQL (SPOT
    // constant), then asserts content IS NULL on disk and content_enc round-trips.
    [Fact]
    public async Task CutoverNullOut_DualStateRow_ClearsPlaintext_ContentEncRoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = RichContent("Dual-state Person NO-10");

        var (resumeId, versionId, owner) = await SeedEncryptedMasterAsync(original, ct);

        // Force the dual-state the backfill produced: a real content_enc ciphertext
        // row that ALSO still carries legacy plaintext `content` (raw write bypasses
        // the EF write-ignore).
        var legacyJson = JsonSerializer.Serialize(original, CanonicalJson);
        using (var seedScope = _fixture.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var conn = seedDb.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"UPDATE resume_versions SET content = CAST(@c AS jsonb) " +
                $"WHERE id = '{versionId.Value}'";
            AddParam(cmd, "@c", legacyJson);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Arrange guard: the row is genuinely dual-state before the null-out.
        using (var preScope = _fixture.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await RawScalarAsync(preDb,
                    $"SELECT content FROM resume_versions WHERE id = '{versionId.Value}'",
                    ct))
                .ShouldNotBeNull("arrange: legacy content is present pre-null-out");
        }

        // Act: run the exact migration null-out SQL (SPOT constant).
        using (var actScope = _fixture.Services.CreateScope())
        {
            var actDb = actScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await actDb.Database.ExecuteSqlRawAsync(
                NullResumeVersionLegacyContent.NullOutSql, ct);
        }

        // Assert (a): legacy plaintext is gone on disk.
        using (var verifyScope = _fixture.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await RawScalarAsync(verifyDb,
                    $"SELECT content FROM resume_versions WHERE id = '{versionId.Value}'",
                    ct))
                .ShouldBeNull("null-out cleared legacy plaintext for the content_enc row");
        }

        // Assert (b): content_enc still round-trips to the original CV content.
        using (var readScope = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(readScope, owner, ct);
            var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var resume = await readDb.Resumes
                .AsNoTracking().Include(r => r.Versions)
                .SingleAsync(r => r.Id == resumeId, ct);
            var loaded = resume.MasterVersion.Content;
            loaded.ShouldNotBeNull("content_enc must still decrypt after the null-out");
            ShouldDeepEqual(loaded, original);
        }
    }

    // ── 5. Fail-closed (KMS down on write) ──────────────────────────────
    [Fact]
    public async Task KmsFailOnWrite_FailsClosed_NoRowPersisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var content = RichContent("Får ALDRIG nå disken som klartext W5");

        var failingKms = Substitute.For<IAmazonKeyManagementService>();
        failingKms
            .GenerateDataKeyAsync(
                Arg.Any<GenerateDataKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GenerateDataKeyResponse>>(_ =>
                throw new AmazonKeyManagementServiceException(
                    "KMS GenerateDataKey nere"));

        await using var failGraph =
            FailingKmsGraph.Build(_fixture.ConnectionString, failingKms);

        ResumeId resumeId;
        using (var scope = failGraph.Provider.CreateScope())
        {
            // Primärt: prefetch-steget (= FieldEncryptionKeyPrefetchBehavior,
            // före UnitOfWork) kastar — KMS GenerateDataKey nere ⇒ inget save.
            var store = scope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
            var owner = scope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
            owner.SetOwner(seeker.Id);

            var prefetchEx = await Record.ExceptionAsync(async () =>
                await store.GetOrCreateDataKeyAsync(seeker.Id, ct));
            prefetchEx.ShouldNotBeNull(
                "KMS GenerateDataKey-fel måste propageras i prefetch-steget " +
                "(fail-closed FÖRE save — Approach A)");

            // Sekundärt: save UTAN varm cache ⇒ SaveChangesInterceptorn kastar
            // CryptographicException (fail-closed), ingen klartext-DML.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var resume = Resume.Create(
                seeker.Id, "C4.4-Fail-W", "Anna Andersson",
                new FixedClock(DateTimeOffset.UtcNow)).Value;
            resume.UpdateMasterContent(content, new FixedClock(DateTimeOffset.UtcNow))
                .IsSuccess.ShouldBeTrue();
            resumeId = resume.Id;
            db.Resumes.Add(resume);

            var saveEx = await Record.ExceptionAsync(async () =>
                await db.SaveChangesAsync(ct));
            saveEx.ShouldNotBeNull();
            saveEx.ShouldBeOfType<CryptographicException>(
                "save utan varm DEK-cache måste fail-closed:a i " +
                "FieldEncryptionSaveChangesInterceptor (kasta FÖRE DML)");
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rowCount = await RawScalarAsync(
            verifyDb,
            $"SELECT count(*) FROM resumes WHERE id = '{resumeId.Value}'",
            ct);
        rowCount.ShouldBe("0",
            "KMS-fel vid save måste fail-closed:a — ingen rad får persisteras");
    }

    // ── 6. Fail-closed (KMS down on read) ───────────────────────────────
    [Fact]
    public async Task KmsFailOnRead_FailsClosed_NoPlaintextReturned()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = RichContent("Krypterad rad som inte får läcka R6");

        var (resumeId, _, owner) = await SeedEncryptedMasterAsync(original, ct);

        var failingKms = Substitute.For<IAmazonKeyManagementService>();
        failingKms
            .DecryptAsync(Arg.Any<DecryptRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DecryptResponse>>(_ =>
                throw new AmazonKeyManagementServiceException("KMS Decrypt nere"));

        await using var failGraph =
            FailingKmsGraph.Build(_fixture.ConnectionString, failingKms);

        using var readScope = failGraph.Provider.CreateScope();

        // Väg 1: simulerat prefetch-steg → KMS Decrypt nere ⇒ kastar FÖRE
        // materialisering.
        var store = readScope.ServiceProvider.GetRequiredService<IUserDataKeyStore>();
        var ownerCtx = readScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
        ownerCtx.SetOwner(owner);

        var prefetchEx = await Record.ExceptionAsync(async () =>
            await store.GetOrCreateDataKeyAsync(owner, ct));
        prefetchEx.ShouldNotBeNull(
            "KMS Decrypt-fel i prefetch-steget måste propageras (fail-closed " +
            "INNAN materialisering)");

        // Väg 2: materialisering UTAN cachad DEK men MED satt owner kastar
        // CryptographicException — ingen ciphertext/null returneras oläst.
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Resume? loaded = null;
        var materializeEx = await Record.ExceptionAsync(async () =>
            loaded = await readDb.Resumes
                .AsNoTracking().Include(r => r.Versions)
                .SingleAsync(r => r.Id == resumeId, ct));

        materializeEx.ShouldNotBeNull(
            "krypterat värde utan cachad DEK måste kasta vid materialisering");
        materializeEx.ShouldBeOfType<CryptographicException>(
            "fail-closed: FieldDecryptionMaterializationInterceptor kastar " +
            "CryptographicException när ägar-DEK saknas i scope-cachen (autentiserad)");
        loaded.ShouldBeNull(
            "ingen klartext eller null-fallback får returneras vid dekrypt-KMS-fel");
    }

    // ── 7. Cross-user per-användare-DEK ─────────────────────────────────
    [Fact]
    public async Task CrossUser_TwoResumes_DecryptWithCorrectPerUserDek()
    {
        var ct = TestContext.Current.CancellationToken;
        var contentA = RichContent("Användare A CV-innehåll X7");
        var contentB = RichContent("Användare B CV-innehåll X7");

        var (resumeA, _, ownerA) = await SeedEncryptedMasterAsync(contentA, ct);
        var (resumeB, _, ownerB) = await SeedEncryptedMasterAsync(contentB, ct);

        ResumeContent loadedA, loadedB;
        using (var scopeA = _fixture.Services.CreateScope())
        {
            // Värm ENBART A:s DEK (precis som prefetch-behaviorn — owner härleds
            // från currentUser).
            await PrefetchOwnerDekAsync(scopeA, ownerA, ct);
            var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
            var rA = await dbA.Resumes.AsNoTracking().Include(r => r.Versions)
                .SingleAsync(r => r.Id == resumeA, ct);
            loadedA = rA.MasterVersion.Content;
        }
        using (var scopeB = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scopeB, ownerB, ct);
            var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
            var rB = await dbB.Resumes.AsNoTracking().Include(r => r.Versions)
                .SingleAsync(r => r.Id == resumeB, ct);
            loadedB = rB.MasterVersion.Content;
        }

        ShouldDeepEqual(loadedA, contentA);
        ShouldDeepEqual(loadedB, contentB);
        loadedA.PersonalInfo.FullName.ShouldBe("Användare A CV-innehåll X7",
            "A:s innehåll ska dekrypteras med A:s DEK — ingen DEK-förväxling");
        loadedB.PersonalInfo.FullName.ShouldBe("Användare B CV-innehåll X7",
            "B:s innehåll ska dekrypteras med B:s DEK — ingen DEK-förväxling");
    }

    // ── 8. System-scope passthrough + regressionsskydd ──────────────────
    [Fact]
    public async Task SystemScope_NoOwner_EncryptedRow_ContentStaysNull_NoThrow_AuthStillThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = RichContent("System-scope CV — ska förbli okrypterat-otillgängligt S8");

        var (resumeId, _, owner) = await SeedEncryptedMasterAsync(original, ct);

        // (a) System-scope: INGEN prefetch ⇒ tom DEK-cache OCH
        // ICurrentDataOwner.JobSeekerId == null (HardDeleteAccountsJob/Hangfire-mönster).
        // Krypterad rad ⇒ passthrough: ingen exception, Content stays null
        // (konfidentialitet bevarad, drift kraschar ej).
        using (var systemScope = _fixture.Services.CreateScope())
        {
            var ownerCtx =
                systemScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
            ownerCtx.JobSeekerId.ShouldBeNull(
                "system-scope: ingen owner satt (vakt mot felaktig fixtur-default)");
            var db = systemScope.ServiceProvider.GetRequiredService<AppDbContext>();

            Resume? loaded = null;
            await Should.NotThrowAsync(async () =>
                loaded = await db.Resumes
                    .AsNoTracking().Include(r => r.Versions)
                    .SingleAsync(r => r.Id == resumeId, ct));
            loaded.ShouldNotBeNull();
            var version = loaded.Versions.ShouldHaveSingleItem();
            version.Content.ShouldBeNull(
                "system-scope + krypterad rad utan DEK ⇒ Content stays null " +
                "(passthrough; ciphertext exponeras ALDRIG som plaintext)");
        }

        // (b) Regressionsskydd: autentiserad scope (owner satt) men UTAN varm
        // DEK på SAMMA krypterade rad ⇒ fortfarande CryptographicException
        // (CTO #3 (iv) ej uppluckrad för Resume-vägen).
        using (var authScope = _fixture.Services.CreateScope())
        {
            var ownerCtx =
                authScope.ServiceProvider.GetRequiredService<ICurrentDataOwner>();
            ownerCtx.SetOwner(owner);
            ownerCtx.JobSeekerId.ShouldNotBeNull(
                "förutsättning: autentiserad scope (owner satt) men ingen varm DEK");
            var db = authScope.ServiceProvider.GetRequiredService<AppDbContext>();

            Resume? loaded = null;
            var ex = await Record.ExceptionAsync(async () =>
                loaded = await db.Resumes
                    .AsNoTracking().Include(r => r.Versions)
                    .SingleAsync(r => r.Id == resumeId, ct));

            ex.ShouldNotBeNull();
            ex.ShouldBeOfType<CryptographicException>(
                "autentiserad ägar-scope utan cachad DEK måste fail-closed:a — " +
                "CTO #3 (iv) får ALDRIG ge tyst ciphertext på Resume-vägen");
            loaded.ShouldBeNull();
        }
    }

    // ── Hjälpare ────────────────────────────────────────────────────────

    /// <summary>
    /// Rå INSERT förbi interceptor-paret av en pre-migrerings-rad:
    /// <c>resumes</c>-parent + en Master-<c>resume_versions</c>-rad med
    /// klartext-JSON i <c>content</c>-jsonb och <c>content_enc</c> NULL.
    /// Kolumn-listorna matchar ResumeConfiguration/ResumeVersionConfiguration
    /// (snake_case; Kind lagras som string-namn; deleted_at NULL ⇒ passerar
    /// global query-filter). xmin är PG-systemkolumn (auto, ingen INSERT).
    /// </summary>
    private async Task<(ResumeId ResumeId, ResumeVersionId VersionId)>
        RawInsertLegacyResumeAsync(
            JobSeekerId jobSeekerId, string legacyContentJson, CancellationToken ct)
    {
        var resumeId = ResumeId.New();
        var versionId = ResumeVersionId.New();

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                """
                INSERT INTO resumes
                    (id, job_seeker_id, name, created_at, updated_at)
                VALUES
                    (@id, @js, @name, now(), now())
                """;
            AddParam(cmd, "@id", resumeId.Value);
            AddParam(cmd, "@js", jobSeekerId.Value);
            AddParam(cmd, "@name", "Legacy-CV (pre-migrering)");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = conn.CreateCommand())
        {
            // content är jsonb ⇒ cast:a parametern. content_enc lämnas NULL
            // (pre-C4.2-rad: ingen ciphertext, ingen sentinel).
            cmd.CommandText =
                """
                INSERT INTO resume_versions
                    (id, resume_id, kind, content, content_enc,
                     created_at, updated_at)
                VALUES
                    (@id, @rid, 'Master', CAST(@content AS jsonb), NULL,
                     now(), now())
                """;
            AddParam(cmd, "@id", versionId.Value);
            AddParam(cmd, "@rid", resumeId.Value);
            AddParam(cmd, "@content", legacyContentJson);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return (resumeId, versionId);
    }

    /// <summary>
    /// Speglar <see cref="WorkerTestFixture"/>:s DI-graf men sista-vinner-
    /// registrerar en valfri (typiskt failing) KMS-klient. ENDAST publika
    /// produktionsregistreringar (<c>AddPersistence</c> registrerar C4.2-
    /// interceptor-paret + DbContext + KMS-graf). Samma migrations-DB (delad
    /// container) så rader skrivna här syns i fixturens verify-scope.
    /// Mönster: <c>FieldEncryptionInterceptorTests.FailingKmsGraph</c>.
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
}
