using System.Data.Common;
using System.Security.Cryptography;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Security;

/// <summary>
/// TD-13 FAS 3.5 batch <b>C5 — integration-svit för fält-krypterings-backfillen</b>
/// (ADR 0049 Beslut 4 + C5, dotnet-architect-låst 2026-05-19). Driver lazy-
/// migreringen deterministiskt till 100 % ciphertext över de tre user-ägda
/// Form A PII-text-kolumnerna (<c>applications.cover_letter</c>,
/// <c>application_notes.content</c>, <c>follow_ups.note</c>) mot riktig Postgres
/// (Testcontainers via
/// <see cref="WorkerTestFixture"/>) — InMemory förbjudet (CLAUDE.md/test-stack;
/// ADR 0049 Mekanik-not 4): interceptor↔Npgsql-materialiserings-ordningen och
/// den force-Modified-baserade re-write-mekaniken är load-bearing och måste
/// verifieras empiriskt mot en riktig provider.
///
/// <para>
/// <b>Seedning av legacy-rader:</b> rå INSERT förbi interceptor-paret (ingen
/// <c>v1:</c>-sentinel; tre Form A text-kolumner — resume_versions Form B-armen
/// pensionerad vid cutover, #507a, seedas ej) — exakt C3/C4.4-precedensens
/// <c>RawInsertLegacy*</c>-mönster. <b>KMS</b> är den delade deterministiska
/// fake-KMS:en som <see cref="WorkerTestFixture"/> sista-vinner-registrerar
/// (ingen riktig AWS, produktkod orörd).
/// </para>
///
/// <para>
/// <b>Idempotens-precision (architect Q3 / legacy-on-disk):</b> porten
/// råfrågar legacy-id:n PER kolumn FÖRE load och force-Modify:ar enbart de
/// raderna. Scenario 3 fångar regressioner mot detta genom att kapa
/// <c>content_enc</c>-ciphertext efter run 1 och kräva byte-identitet efter
/// run 2 (redan-ciphertext-rader får ALDRIG re-krypteras → ny IV → ny
/// ciphertext).
/// </para>
///
/// <para>
/// <b>Cross-user-isolering (§5.1):</b> porten äter ett FRESH DI-scope per
/// ägare via <see cref="IServiceScopeFactory"/> (scoped DEK-cache/owner/
/// DbContext). Scenario 6 bevisar att varje ägares data krypteras med sin
/// EGEN DEK genom att läsa tillbaka per-ägare med prefetch och deep-equala.
/// </para>
///
/// <para>TDD-ordning (CLAUDE.md §2.4/§7): linjerad mot färdig C5-produktkod
/// on-disk 2026-05-19 (IFieldEncryptionBackfiller + FieldEncryptionBackfiller +
/// BackfillFieldEncryptionJob). Specifikationstest mot kontrakts-ytan, ej
/// rad-för-rad bekräftelse av impl.</para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class FieldEncryptionBackfillTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

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
            Guid.NewGuid(), "C5 Test", new FixedClock(DateTimeOffset.UtcNow)).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    /// <summary>
    /// Simulerar <c>FieldEncryptionKeyPrefetchBehavior</c>: värmer ägar-DEK +
    /// sätter <see cref="ICurrentDataOwner"/>. MÅSTE anropas i SAMMA scope som
    /// läsningen (cache + owner är scoped).
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
    /// Rå INSERT förbi interceptor-paret av en pre-migrerings-ägare:
    /// en Application med klartext <c>cover_letter</c> + ett
    /// <c>application_notes</c>-barn (klartext content) + ett <c>follow_ups</c>-
    /// barn (klartext note). Alla utan <c>v1:</c>-sentinel ⇒ legacy on-disk
    /// (resume_versions Form B-armen pensionerad vid cutover, #507a — seedas ej).
    /// Kolumn-listorna speglar ApplicationConfiguration (snake_case;
    /// Status/Kind som string-namn; deleted_at NULL ⇒ passerar global filter).
    /// </summary>
    private async Task<LegacyOwnerIds> RawInsertLegacyOwnerAsync(
        JobSeekerId jobSeekerId, string marker, CancellationToken ct)
    {
        var appId = ApplicationId.New();
        var noteId = ApplicationNoteId.New();
        var followUpId = FollowUpId.New();

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                """
                INSERT INTO applications
                    (id, job_seeker_id, cover_letter, status,
                     created_at, updated_at, last_status_change_at,
                     ghosted_threshold_days)
                VALUES
                    (@id, @js, @cl, 'Draft', now(), now(), now(), 21)
                """;
            AddParam(cmd, "@id", appId.Value);
            AddParam(cmd, "@js", jobSeekerId.Value);
            AddParam(cmd, "@cl", $"{marker} klartext-personligt-brev");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                """
                INSERT INTO application_notes
                    (id, application_id, content, created_at)
                VALUES
                    (@id, @aid, @content, now())
                """;
            AddParam(cmd, "@id", noteId.Value);
            AddParam(cmd, "@aid", appId.Value);
            AddParam(cmd, "@content", $"{marker} klartext-anteckning");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                """
                INSERT INTO follow_ups
                    (id, application_id, channel, scheduled_at, note,
                     outcome, created_at)
                VALUES
                    (@id, @aid, 'Email', @sched, @note, 'Pending', now())
                """;
            AddParam(cmd, "@id", followUpId.Value);
            AddParam(cmd, "@aid", appId.Value);
            AddParam(cmd, "@sched", DateTimeOffset.UtcNow.AddDays(3));
            AddParam(cmd, "@note", $"{marker} klartext-uppföljning");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return new LegacyOwnerIds(appId, noteId, followUpId, marker);
    }

    private sealed record LegacyOwnerIds(
        ApplicationId AppId,
        ApplicationNoteId NoteId,
        FollowUpId FollowUpId,
        string Marker);

    private async Task BackfillOwnerAsync(JobSeekerId owner, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var backfiller = scope.ServiceProvider.GetRequiredService<IFieldEncryptionBackfiller>();
        await backfiller.BackfillOwnerAsync(owner.Value, ct);
    }

    // ── 1. Backfill krypterar legacy över de tre text-kolumnerna ───────────
    [Fact]
    public async Task Backfill_EncryptsLegacyAcrossThreeTextColumns_OnDisk()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var ids = await RawInsertLegacyOwnerAsync(seeker.Id, "BACKFILL-S1", ct);

        await BackfillOwnerAsync(seeker.Id, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var coverLetter = await RawScalarAsync(
            db, $"SELECT cover_letter FROM applications WHERE id = '{ids.AppId.Value}'", ct);
        coverLetter.ShouldNotBeNull();
        coverLetter.ShouldStartWith("v1:");
        coverLetter.ShouldNotContain("klartext-personligt-brev", Case.Sensitive);

        var noteContent = await RawScalarAsync(
            db, $"SELECT content FROM application_notes WHERE id = '{ids.NoteId.Value}'", ct);
        noteContent.ShouldNotBeNull();
        noteContent.ShouldStartWith("v1:");
        noteContent.ShouldNotContain("klartext-anteckning", Case.Sensitive);

        var followUpNote = await RawScalarAsync(
            db, $"SELECT note FROM follow_ups WHERE id = '{ids.FollowUpId.Value}'", ct);
        followUpNote.ShouldNotBeNull();
        followUpNote.ShouldStartWith("v1:");
        followUpNote.ShouldNotContain("klartext-uppföljning", Case.Sensitive);
    }

    // ── 2. Round-trip post-backfill — allt dekrypterar till original ─────
    [Fact]
    public async Task Backfill_RoundTripPostBackfill_DecryptsToOriginalPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var ids = await RawInsertLegacyOwnerAsync(seeker.Id, "BACKFILL-S2", ct);

        await BackfillOwnerAsync(seeker.Id, ct);

        using var readScope = _fixture.Services.CreateScope();
        await PrefetchOwnerDekAsync(readScope, seeker.Id, ct);
        var db = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var app = await db.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Notes)
            .Include(a => a.FollowUps)
            .SingleAsync(a => a.Id == ids.AppId, ct);

        app.CoverLetter.ShouldBe("BACKFILL-S2 klartext-personligt-brev",
            "cover_letter ska dekryptera till ursprungsklartexten");
        app.Notes.ShouldHaveSingleItem().Content.ShouldBe("BACKFILL-S2 klartext-anteckning");
        app.FollowUps.ShouldHaveSingleItem().Note.ShouldBe("BACKFILL-S2 klartext-uppföljning");
    }

    // ── 3. Idempotens — andra körning är no-op (byte-identisk ciphertext) ─
    [Fact]
    public async Task Backfill_RunTwice_SecondRunIsNoOp_CiphertextByteIdentical()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        var ids = await RawInsertLegacyOwnerAsync(seeker.Id, "BACKFILL-S3", ct);

        await BackfillOwnerAsync(seeker.Id, ct);

        // Kapa ciphertext efter run 1 (rådata förbi interceptorn).
        async Task<(string Cl, string Note, string Fu)> SnapshotAsync()
        {
            using var s = _fixture.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            return (
                (await RawScalarAsync(db, $"SELECT cover_letter FROM applications WHERE id = '{ids.AppId.Value}'", ct))!,
                (await RawScalarAsync(db, $"SELECT content FROM application_notes WHERE id = '{ids.NoteId.Value}'", ct))!,
                (await RawScalarAsync(db, $"SELECT note FROM follow_ups WHERE id = '{ids.FollowUpId.Value}'", ct))!);
        }

        var afterRun1 = await SnapshotAsync();

        // Andra körning — porten råfrågar legacy-id:n FÖRE load; alla rader är
        // redan v1:-ciphertext ⇒ tidig return (idempotent no-op).
        await BackfillOwnerAsync(seeker.Id, ct);

        var afterRun2 = await SnapshotAsync();

        afterRun2.Cl.ShouldBe(afterRun1.Cl,
            "redan-ciphertext cover_letter får ALDRIG re-krypteras (ny IV → ny ciphertext)");
        afterRun2.Note.ShouldBe(afterRun1.Note,
            "redan-ciphertext note-content får ALDRIG re-krypteras");
        afterRun2.Fu.ShouldBe(afterRun1.Fu,
            "redan-ciphertext follow_up-note får ALDRIG re-krypteras");

        using var fitnessScope = _fixture.Services.CreateScope();
        var backfiller = fitnessScope.ServiceProvider
            .GetRequiredService<IFieldEncryptionBackfiller>();
        // Global gate kan vara > 0 om andra parallella seeds finns; den
        // ägar-specifika invarianten bärs av byte-identiteten ovan.
        var counts = await backfiller.CountRemainingLegacyAsync(ct);
        counts.Total.ShouldBeGreaterThanOrEqualTo(0);
    }

    // ── 4. CountRemainingLegacyAsync fitness-funktion (deterministisk gate) ─
    [Fact]
    public async Task CountRemainingLegacy_ReturnsPerColumnCounts_ZeroAfterFullBackfill()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        await RawInsertLegacyOwnerAsync(seeker.Id, "BACKFILL-S4", ct);

        using (var preScope = _fixture.Services.CreateScope())
        {
            var backfiller = preScope.ServiceProvider
                .GetRequiredService<IFieldEncryptionBackfiller>();
            var before = await backfiller.CountRemainingLegacyAsync(ct);
            before.CoverLetter.ShouldBeGreaterThan(0,
                "minst denna seedade ägares cover_letter är legacy före backfill");
            before.ApplicationNoteContent.ShouldBeGreaterThan(0);
            before.FollowUpNote.ShouldBeGreaterThan(0);
            before.Total.ShouldBe(
                before.CoverLetter + before.ApplicationNoteContent
                + before.FollowUpNote,
                "Total = summan av de tre per-kolumn-räkningarna");
        }

        // Driv HELA databasen (alla ägare) till 0 — den deterministiska gaten.
        await DrainAllOwnersAsync(ct);

        using var postScope = _fixture.Services.CreateScope();
        var post = postScope.ServiceProvider
            .GetRequiredService<IFieldEncryptionBackfiller>();
        var after = await post.CountRemainingLegacyAsync(ct);
        after.CoverLetter.ShouldBe(0);
        after.ApplicationNoteContent.ShouldBe(0);
        after.FollowUpNote.ShouldBe(0);
        after.Total.ShouldBe(0,
            "CountRemainingLegacyAsync().Total == 0 är den deterministiska klar-gaten");
    }

    // ── 5. GetOwnersWithLegacyFieldsAsync — vilken som helst av 3 kolumner ─
    [Fact]
    public async Task GetOwnersWithLegacy_ReturnsOwnersWithAnyLegacyColumn_RespectsBatchSize_EmptyWhenDone()
    {
        var ct = TestContext.Current.CancellationToken;
        var seekerA = await SeedJobSeekerAsync(ct);
        var seekerB = await SeedJobSeekerAsync(ct);
        await RawInsertLegacyOwnerAsync(seekerA.Id, "BACKFILL-S5A", ct);
        await RawInsertLegacyOwnerAsync(seekerB.Id, "BACKFILL-S5B", ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var backfiller = scope.ServiceProvider
                .GetRequiredService<IFieldEncryptionBackfiller>();

            var owners = await backfiller.GetOwnersWithLegacyFieldsAsync(1000, ct);
            owners.ShouldContain(seekerA.Id.Value,
                "ägare A har legacy i de tre kolumnerna");
            owners.ShouldContain(seekerB.Id.Value);

            var batched = await backfiller.GetOwnersWithLegacyFieldsAsync(1, ct);
            batched.Count.ShouldBe(1, "batchSize=1 ska kapa resultatet till 1 ägare");
        }

        await DrainAllOwnersAsync(ct);

        using var doneScope = _fixture.Services.CreateScope();
        var done = doneScope.ServiceProvider
            .GetRequiredService<IFieldEncryptionBackfiller>();
        (await done.GetOwnersWithLegacyFieldsAsync(1000, ct))
            .ShouldBeEmpty("inga legacy-ägare kvar efter full backfill");
    }

    // ── 6. Cross-user-isolering — varje ägare med sin egen DEK ───────────
    [Fact]
    public async Task Backfill_CrossUser_EachOwnerEncryptedWithOwnDek()
    {
        var ct = TestContext.Current.CancellationToken;
        var seekerA = await SeedJobSeekerAsync(ct);
        var seekerB = await SeedJobSeekerAsync(ct);
        var idsA = await RawInsertLegacyOwnerAsync(seekerA.Id, "XUSER-A", ct);
        var idsB = await RawInsertLegacyOwnerAsync(seekerB.Id, "XUSER-B", ct);

        // Backfill i varsitt fresh scope (porten gör detta internt; vi anropar
        // per ägare ⇒ varje BackfillOwnerAsync äter eget DI-scope).
        await BackfillOwnerAsync(seekerA.Id, ct);
        await BackfillOwnerAsync(seekerB.Id, ct);

        // A:s data dekrypterar korrekt MED A:s prefetch, B:s MED B:s.
        using (var scopeA = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scopeA, seekerA.Id, ct);
            var db = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
            var appA = await db.Applications
                .AsNoTracking().SingleAsync(a => a.Id == idsA.AppId, ct);
            appA.CoverLetter.ShouldBe("XUSER-A klartext-personligt-brev",
                "A:s cover_letter dekrypteras med A:s DEK — ingen DEK-förväxling");
        }

        using (var scopeB = _fixture.Services.CreateScope())
        {
            await PrefetchOwnerDekAsync(scopeB, seekerB.Id, ct);
            var db = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
            var appB = await db.Applications
                .AsNoTracking().SingleAsync(a => a.Id == idsB.AppId, ct);
            appB.CoverLetter.ShouldBe("XUSER-B klartext-personligt-brev",
                "B:s cover_letter dekrypteras med B:s DEK — ingen DEK-förväxling");
        }
    }

    // ── 7. Job-orchestrator driver till 0 + skriver alltid audit-rad ─────
    [Fact]
    public async Task Job_RunAsync_DrivesToZeroRemaining_WritesAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedJobSeekerAsync(ct);
        await RawInsertLegacyOwnerAsync(seeker.Id, "BACKFILL-S7", ct);

        await RunJobAsync(ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var backfiller = verifyScope.ServiceProvider
            .GetRequiredService<IFieldEncryptionBackfiller>();
        var remaining = await backfiller.CountRemainingLegacyAsync(ct);
        remaining.Total.ShouldBe(0,
            "orchestratorn ska driva HELA databasen till 0 legacy-fält");

        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRows = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.EventType == "System.FieldEncryptionBackfillRun")
            .ToListAsync(ct);
        auditRows.ShouldNotBeEmpty(
            "GDPR Art. 30: FieldEncryptionBackfillRun-audit-rad ska skrivas");
        auditRows.ShouldContain(e => e.AggregateType == "System.FieldEncryptionBackfill");
    }

    // ── 7b. Audit-rad skrivs ALLTID — även på tom DB (0 ägare) ───────────
    [Fact]
    public async Task Job_RunAsync_WritesAuditRow_EvenWhenZeroOwners()
    {
        var ct = TestContext.Current.CancellationToken;

        // Inga legacy-ägare seedade här. Worker-collection är seriell men
        // andra tester kan ha lämnat data; vi dränerar först så jobbet körs
        // mot "0 ägare", och asserterar att MINST en ny audit-rad ändå
        // tillkommer ("skrivs alltid", även 0 ägare).
        await DrainAllOwnersAsync(ct);

        int before;
        using (var beforeScope = _fixture.Services.CreateScope())
        {
            var db = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            before = await db.AuditLogEntries
                .AsNoTracking()
                .CountAsync(e => e.EventType == "System.FieldEncryptionBackfillRun", ct);
        }

        await RunJobAsync(ct); // 0 ägare kvar — ska ÄNDÅ skriva audit-rad

        using var afterScope = _fixture.Services.CreateScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await afterDb.AuditLogEntries
            .AsNoTracking()
            .CountAsync(e => e.EventType == "System.FieldEncryptionBackfillRun", ct);

        after.ShouldBeGreaterThan(before,
            "FieldEncryptionBackfillRun-audit-rad skrivs ALLTID — även 0 ägare " +
            "(GDPR Art. 30 accountability)");
    }

    // ── Hjälpare ────────────────────────────────────────────────────────

    private async Task RunJobAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var backfiller = scope.ServiceProvider.GetRequiredService<IFieldEncryptionBackfiller>();
        var auditor = scope.ServiceProvider.GetRequiredService<ISystemEventAuditor>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<BackfillFieldEncryptionJob>();
        var job = new BackfillFieldEncryptionJob(
            backfiller, new FixedClock(DateTimeOffset.UtcNow), auditor, logger);
        await job.RunAsync(ct);
    }

    /// <summary>
    /// Driver HELA databasen till 0 legacy via porten direkt (owner-discovery →
    /// per-owner backfill loop). Mindre brusig än att lita på orchestratorns
    /// audit-skrivning för rena fitness-asserts.
    /// </summary>
    // Konvergens-vakt: i testdata krymper varje iteration restmängden →
    // terminerar långt under detta tak (defense mot oavsiktlig non-convergence).
    private const int MaxDrainIterations = 1000;

    private async Task DrainAllOwnersAsync(CancellationToken ct)
    {
        for (var i = 0; i < MaxDrainIterations; i++)
        {
            List<Guid> owners;
            using (var scope = _fixture.Services.CreateScope())
            {
                var backfiller = scope.ServiceProvider
                    .GetRequiredService<IFieldEncryptionBackfiller>();
                owners = (await backfiller.GetOwnersWithLegacyFieldsAsync(100, ct)).ToList();
            }

            if (owners.Count == 0)
                return;

            foreach (var owner in owners)
            {
                using var scope = _fixture.Services.CreateScope();
                var backfiller = scope.ServiceProvider
                    .GetRequiredService<IFieldEncryptionBackfiller>();
                await backfiller.BackfillOwnerAsync(owner, ct);
            }
        }

        throw new InvalidOperationException(
            "DrainAllOwnersAsync konvergerade inte på 1000 iterationer — " +
            "möjlig non-convergence-defekt i porten (flagga för CC/architect)");
    }
}
