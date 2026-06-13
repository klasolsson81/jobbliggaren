namespace Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4) — port mot fält-krypterings-backfillen.
/// Implementeras i Infrastructure (äger AppDbContext + per-användare-DEK +
/// interceptor-interplay; <see cref="Jobbliggaren.Application"/> förblir
/// EF-/krypto-fri, Clean Arch / ADR 0009). Paritet med
/// <c>IAccountHardDeleter</c> — porten exponerar primitiv <see cref="Guid"/>
/// (JobSeekerId är Domain), impl wrappar internt.
///
/// <para>
/// Driver lazy-migreringen deterministiskt till 100 % ciphertext över de fyra
/// user-ägda PII-kolumnerna (cover_letter / application_notes.content /
/// follow_ups.note / resume_versions.content_enc). Bounded, idempotent,
/// cancellation-bar (Ford/Parsons/Kua 2017 — migration med deterministiskt
/// slut). Cutover-flippen (Beslut 5 steg 3) är INTE denna ports ansvar — den
/// är en separat Klas-STOPP-migration.
/// </para>
/// </summary>
public interface IFieldEncryptionBackfiller
{
    /// <summary>
    /// Distinkta JobSeeker-id (max <paramref name="batchSize"/>) som har minst
    /// en legacy (icke-ciphertext) PII-rad i någon av de fyra kolumnerna.
    /// Read-only, system-scope (ingen DEK). Krymper monotont per backfill-batch
    /// → bounded yttre loop.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetOwnersWithLegacyFieldsAsync(
        int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Krypterar alla legacy PII-fält för en ägare i ett eget DI-scope
    /// (per-owner-isolering — ingen cross-user-DEK-läcka, §5.1). Värmer
    /// ägar-DEK (replikerar <c>FieldEncryptionKeyPrefetchBehavior</c>) FÖRE
    /// load/save så encrypt-on-write-interceptorn har varm DEK. Idempotent:
    /// rör endast rader som är legacy on-disk (redan-ciphertext orörda).
    /// </summary>
    Task BackfillOwnerAsync(Guid jobSeekerId, CancellationToken cancellationToken);

    /// <summary>
    /// Fitness-funktion (ADR 0049 Validering; ADR 0045 observe-only-ratchet):
    /// per-kolumn antal kvarvarande legacy-rader. Backfillen är klar när
    /// <see cref="LegacyFieldCounts.Total"/> == 0 (deterministisk gate mot
    /// permanent dual-state). Cutover-flippen vid 0 är separat Klas-STOPP.
    /// </summary>
    Task<LegacyFieldCounts> CountRemainingLegacyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// TD-13 (ADR 0049) — per-kolumn legacy-räkning (fitness). Value object
/// (CLAUDE.md §3.3 — ej fyra parallella lösa <see cref="long"/>).
/// </summary>
public readonly record struct LegacyFieldCounts(
    long CoverLetter,
    long ApplicationNoteContent,
    long FollowUpNote,
    long ResumeVersionContent)
{
    public long Total =>
        CoverLetter + ApplicationNoteContent + FollowUpNote + ResumeVersionContent;
}
