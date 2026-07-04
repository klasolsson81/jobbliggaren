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
/// Drives the lazy migration deterministically to 100% ciphertext over the
/// three user-owned Form A PII text columns (cover_letter /
/// application_notes.content / follow_ups.note). Bounded, idempotent,
/// cancellation-aware (Ford/Parsons/Kua 2017 — migration with a deterministic
/// end). The resume_versions Form B arm was RETIRED at cutover (#507a / ADR
/// 0049 Beslut 5 steg 3): the read-interceptor plaintext fallback it depended
/// on to materialize legacy content before re-encryption is gone, content_enc
/// is now the sole source, and there is no legacy resume content left to
/// migrate (the fitness gate proved 0 before the mapping flip).
/// </para>
/// </summary>
public interface IFieldEncryptionBackfiller
{
    /// <summary>
    /// Distinkta JobSeeker-id (max <paramref name="batchSize"/>) som har minst
    /// en legacy (icke-ciphertext) PII-rad i någon av de tre Form A-kolumnerna
    /// (resume_versions Form B-armen pensionerad vid cutover, #507a).
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
/// TD-13 (ADR 0049) — per-column legacy count (fitness). Value object
/// (CLAUDE.md §3.3 — not three loose parallel <see cref="long"/>). The
/// resume_versions Form B count was retired at cutover (#507a); only the three
/// Form A text columns remain.
/// </summary>
public readonly record struct LegacyFieldCounts(
    long CoverLetter,
    long ApplicationNoteContent,
    long FollowUpNote)
{
    public long Total =>
        CoverLetter + ApplicationNoteContent + FollowUpNote;
}
