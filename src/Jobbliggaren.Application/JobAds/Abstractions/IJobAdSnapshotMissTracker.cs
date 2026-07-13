using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Application-port för snapshot-miss-bokföring (ADR 0032-amendment 2026-05-23).
/// Spårar hur många konsekutiva snapshot-runs en JobAd:s ExternalId har saknats
/// — defense-in-depth mot snapshot-trunkering (ADR 0032-amendment 2026-05-16):
/// en enskild trunkerad run får aldrig stänga jobb felaktigt; N=3 konsekutiva
/// misses krävs innan retention arkiverar.
/// <para>
/// <b>Konsumtions-disciplin (arch-test-låst):</b>
/// <list type="bullet">
/// <item><see cref="ApplyAsync"/> — endast <c>SyncPlatsbankenSnapshotJob</c> (efter komplett snapshot, ej trunkerat).</item>
/// <item><see cref="ArchiveJobAdsWithMissCountAtLeastAsync"/> — endast <c>RetainPlatsbankenJobAdsJob</c>.</item>
/// </list>
/// </para>
/// <para>
/// Paritet <c>IUserDataKeyStore</c> (TD-13 C2): Application-port, Infrastructure-
/// only entity bakom porten, ALDRIG via <c>IAppDbContext</c>-DbSet (ISP, Martin
/// 2017 kap. 10/22; ADR 0009).
/// </para>
/// </summary>
public interface IJobAdSnapshotMissTracker
{
    /// <summary>
    /// Bokför miss-status för en komplett snapshot-run. För ExternalIds i
    /// <paramref name="seenExternalIds"/>: upsert till <c>miss_count=0</c>.
    /// För Active-JobAds med <c>Source=<paramref name="source"/></c>
    /// vars ExternalId INTE finns i set:n: increment <c>miss_count</c> +
    /// uppdatera <c>last_missed_at</c> (sätt <c>first_missed_at</c> om null).
    /// <para>
    /// MÅSTE bara anropas vid komplett (icke-trunkerad) snapshot —
    /// se <see cref="SnapshotOutcome.TruncatedAndExhausted"/>.
    /// </para>
    /// </summary>
    Task<SnapshotMissUpdateResult> ApplyAsync(
        JobSource source,
        IReadOnlySet<string> seenExternalIds,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Arkiverar (Status=Archived) Active-JobAds vars miss-tabell-rad har
    /// <c>miss_count &gt;= <paramref name="threshold"/></c>. Bulk-UPDATE via
    /// <c>ExecuteUpdateAsync</c> — domain-event raisas EJ per item
    /// (CTO-rond 2026-05-23 Q3=B; aggregerad audit-rad via
    /// <c>ISystemEventAuditor</c>).
    /// </summary>
    /// <returns>Antal arkiverade rader.</returns>
    Task<int> ArchiveJobAdsWithMissCountAtLeastAsync(
        JobSource source,
        int threshold,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returnerar största observerade <c>Fetched</c> (snapshot-`ParsedTotal`)
    /// från <c>audit_log</c>:s <c>System.JobAdsSynced</c>-rader med
    /// <c>JobType=snapshot</c>, för given källa, inom senaste
    /// <paramref name="days"/> dygn. Null om ingen audit-historik finns.
    /// Använd för relativ floor-tröskel (CTO-rond 2026-05-23 Q5).
    /// </summary>
    Task<int?> GetMaxObservedSnapshotSizeAsync(
        JobSource source,
        int days,
        CancellationToken cancellationToken);

    /// <summary>
    /// Räknar Active-JobAds med given källa (JobAd har ingen soft-delete-axel, #821). Använt av
    /// post-archive circuit-breaker (CTO-rond 2026-05-23 H1 + security-auditor):
    /// retention-jobbet jämför candidates/active mot
    /// <c>MaxArchivePctPerRun</c> innan <c>ExecuteUpdate</c> för fail-loud-skydd
    /// mot ofog-konfig.
    /// </summary>
    Task<int> CountActiveJobAdsAsync(
        JobSource source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Räknar Active-JobAds med given källa vars
    /// <c>JobAdSnapshotMiss.MissCount &gt;= <paramref name="threshold"/></c>.
    /// Använt av post-archive circuit-breaker (samma som ovan) — antalet
    /// rader som SKULLE arkiveras av <see cref="ArchiveJobAdsWithMissCountAtLeastAsync"/>.
    /// </summary>
    Task<int> CountArchiveCandidatesAsync(
        JobSource source,
        int threshold,
        CancellationToken cancellationToken);
}

/// <summary>
/// Statistik från <see cref="IJobAdSnapshotMissTracker.ApplyAsync"/>.
/// Konsumeras i snapshot-jobbets audit-rad (informativt, ej kritiskt).
/// </summary>
public sealed record SnapshotMissUpdateResult(int ResetCount, int IncrementedCount);
