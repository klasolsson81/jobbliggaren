using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 + C5, dotnet-architect-låst 2026-05-19) —
/// <see cref="IFieldEncryptionBackfiller"/>-impl. Driver lazy-migreringen till
/// 100 % ciphertext över de tre Form A user-ägda PII-text-kolumnerna
/// (resume_versions Form B-armen pensionerad vid cutover, #507a).
///
/// <para>
/// <b>Per-owner fresh DI-scope (load-bearing, §5.1):</b>
/// <see cref="ScopedUserDataKeyCache"/>/<see cref="ICurrentDataOwner"/>/
/// <see cref="AppDbContext"/> är alla Scoped. Att dela scope över owners →
/// DEK-cachen ackumulerar flera ägares DEK → cross-user-läcka. Porten äger
/// därför scope-livscykeln via <see cref="IServiceScopeFactory"/> (paritet
/// med att <c>AccountHardDeleter</c> äger transaktions-livscykeln, ej jobbet)
/// — ett scope per <see cref="BackfillOwnerAsync"/>. Read-only-metoderna
/// (owner-discovery, fitness) är system-scope, ingen DEK.
/// </para>
///
/// <para>
/// <b>Forced-Modified (architect Q3):</b> en legacy-rad materialiseras men
/// markeras inte Modified av EF (inget domän-fält ändrades). Backfillen
/// markerar den krypterade propertyn/shadow:en
/// <c>Entry(e).Property(name).IsModified = true</c> — EF Core 10:s minimala
/// re-write-trigger. Form A: domän-string-property (CoverLetter/Content/Note).
/// (Form B — resume_versions shadow <c>ContentEnc</c> — pensionerad vid cutover,
/// #507a: read-interceptor-fallbacken som armen berodde på för att materialisera
/// legacy-content före omkryptering är borta.) Detta muterar inte
/// domäntillståndet. Encrypt-on-write-interceptorn fyrar (entry Modified) och
/// krypterar; redan-ciphertext-rader markeras INTE (idempotent — re-run no-op).
/// </para>
///
/// <para>
/// <b>Idempotens via legacy-on-disk-precision:</b> read-interceptorn
/// dekrypterar redan-ciphertext-rader till klartext i minnet → man kan inte
/// efter materialisering avgöra vad som var legacy. Porten råfrågar därför
/// legacy-id:n PER kolumn FÖRE load och force-Modify:ar enbart de raderna.
/// </para>
/// </summary>
public sealed class FieldEncryptionBackfiller(
    AppDbContext db,
    IServiceScopeFactory scopeFactory) : IFieldEncryptionBackfiller
{
    // Legacy-detektering: SQL-LIKE-approximation av runtime-auktoriteten
    // IFieldEncryptor.IsEncrypted (regex ^v\d+:, ej LINQ/SQL-översättbar).
    // SSOT = FieldEncryptionSentinel.SqlLikePattern (architect-sanktionerad
    // approximation 2026-05-19; Npgsql översätter EJ
    // string.StartsWith(string, StringComparison) — CA1310-överlagringen).
    // Konstant compile-time-literal interpoleras i parameterlös konstant-
    // predikat-SQL → §5.4-konformt (ingen användardata, ingen injection-yta).
    private const string LegacyLikePredicateValue =
        FieldEncryptionSentinel.SqlLikePattern;

    public async Task<IReadOnlyList<Guid>> GetOwnersWithLegacyFieldsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        // Form A — owner via LINQ (cover_letter / notes.content / follow_ups.note
        // är mappade domän-string-properties; system-scope, ingen DEK).
        var formAOwners = await db.Applications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a =>
                (a.CoverLetter != null && !EF.Functions.Like(a.CoverLetter, LegacyLikePredicateValue))
                || a.Notes.Any(n =>
                    n.Content != null && !EF.Functions.Like(n.Content, LegacyLikePredicateValue))
                || a.FollowUps.Any(f =>
                    f.Note != null && !EF.Functions.Like(f.Note, LegacyLikePredicateValue)))
            .Select(a => a.JobSeekerId.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Form B (resume_versions) retired at cutover (#507a / ADR 0049 Beslut 5
        // steg 3): content_enc is the sole source, so there is no legacy resume
        // content left to discover (the fitness gate proved 0 before the flip).
        return formAOwners
            .Take(batchSize)
            .ToList();
    }

    public async Task BackfillOwnerAsync(
        Guid jobSeekerId, CancellationToken cancellationToken)
    {
        var owner = new JobSeekerId(jobSeekerId);

        // Per-owner fresh scope (cross-user-DEK-isolering, §5.1).
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var scopedDb = sp.GetRequiredService<AppDbContext>();

        // Replikera FieldEncryptionKeyPrefetchBehavior i scopet: värm ägar-DEK
        // + sätt owner FÖRE load/save (decrypt-on-read av redan-ciphertext-
        // rader OCH encrypt-on-write av legacy-rader kräver varm DEK).
        var dataKeyStore = sp.GetRequiredService<IUserDataKeyStore>();
        var currentDataOwner = sp.GetRequiredService<ICurrentDataOwner>();
        currentDataOwner.SetOwner(owner);
        var dek = await dataKeyStore
            .GetOrCreateDataKeyAsync(owner, cancellationToken)
            .ConfigureAwait(false);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(dek);

        // Legacy-id:n PER kolumn FÖRE load (idempotens — endast legacy-on-disk
        // force-Modify:as; redan-ciphertext orörd). Parameteriserad raw SQL.
        var legacyCoverLetterAppIds = await ScalarIdsAsync(
            scopedDb,
            """
            SELECT id AS "Value" FROM applications
            WHERE job_seeker_id = {0}
              AND cover_letter IS NOT NULL AND cover_letter NOT LIKE {1}
            """,
            cancellationToken, jobSeekerId, LegacyLikePredicateValue)
            .ConfigureAwait(false);

        var legacyNoteIds = await ScalarIdsAsync(
            scopedDb,
            """
            SELECT n.id AS "Value" FROM application_notes n
            JOIN applications a ON a.id = n.application_id
            WHERE a.job_seeker_id = {0}
              AND n.content IS NOT NULL AND n.content NOT LIKE {1}
            """,
            cancellationToken, jobSeekerId, LegacyLikePredicateValue)
            .ConfigureAwait(false);

        var legacyFollowUpIds = await ScalarIdsAsync(
            scopedDb,
            """
            SELECT f.id AS "Value" FROM follow_ups f
            JOIN applications a ON a.id = f.application_id
            WHERE a.job_seeker_id = {0}
              AND f.note IS NOT NULL AND f.note NOT LIKE {1}
            """,
            cancellationToken, jobSeekerId, LegacyLikePredicateValue)
            .ConfigureAwait(false);

        if (legacyCoverLetterAppIds.Count == 0
            && legacyNoteIds.Count == 0
            && legacyFollowUpIds.Count == 0)
        {
            return; // Inget legacy kvar för ägaren (idempotent no-op).
        }

        // Tracked load (IgnoreQueryFilters — soft-deletade rader bär också
        // klartext-PII och måste krypteras). Parent måste spåras: write-
        // interceptorns ResolveOwner navigerar barn→spårad parent.
        var applications = await scopedDb.Applications
            .IgnoreQueryFilters()
            .Include(a => a.Notes)
            .Include(a => a.FollowUps)
            .Where(a => a.JobSeekerId == owner)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var coverLetterSet = legacyCoverLetterAppIds.ToHashSet();
        var noteSet = legacyNoteIds.ToHashSet();
        var followUpSet = legacyFollowUpIds.ToHashSet();

        foreach (var app in applications)
        {
            if (coverLetterSet.Contains(app.Id.Value))
                scopedDb.Entry(app).Property(nameof(DomainApplication.CoverLetter))
                    .IsModified = true;

            foreach (var note in app.Notes)
            {
                if (noteSet.Contains(note.Id.Value))
                    scopedDb.Entry(note).Property(nameof(ApplicationNote.Content))
                        .IsModified = true;
            }

            foreach (var followUp in app.FollowUps)
            {
                if (followUpSet.Contains(followUp.Id.Value))
                    scopedDb.Entry(followUp).Property(nameof(FollowUp.Note))
                        .IsModified = true;
            }
        }

        await scopedDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LegacyFieldCounts> CountRemainingLegacyAsync(
        CancellationToken cancellationToken)
    {
        var coverLetter = await db.Applications
            .IgnoreQueryFilters()
            .CountAsync(
                a => a.CoverLetter != null
                     && !EF.Functions.Like(a.CoverLetter, LegacyLikePredicateValue),
                cancellationToken)
            .ConfigureAwait(false);

        var noteContent = await db.Set<ApplicationNote>()
            .IgnoreQueryFilters()
            .CountAsync(
                n => n.Content != null
                     && !EF.Functions.Like(n.Content, LegacyLikePredicateValue),
                cancellationToken)
            .ConfigureAwait(false);

        var followUpNote = await db.Set<FollowUp>()
            .IgnoreQueryFilters()
            .CountAsync(
                f => f.Note != null
                     && !EF.Functions.Like(f.Note, LegacyLikePredicateValue),
                cancellationToken)
            .ConfigureAwait(false);

        return new LegacyFieldCounts(
            CoverLetter: coverLetter,
            ApplicationNoteContent: noteContent,
            FollowUpNote: followUpNote);
    }

    private static async Task<List<Guid>> ScalarIdsAsync(
        AppDbContext scopedDb, string sql, CancellationToken ct,
        params object[] parameters) =>
        await scopedDb.Database
            .SqlQueryRaw<Guid>(sql, parameters)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
