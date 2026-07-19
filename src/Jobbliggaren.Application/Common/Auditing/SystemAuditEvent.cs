namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// Diskriminerad sealed record-hierarki för audit-events från system-jobb.
/// Konsumeras av <see cref="ISystemEventAuditor"/>-impl som serialiserar
/// payloaden till <c>audit_log.payload</c> jsonb-kolumnen. Per ADR 0035.
///
/// <para>
/// <b>EventType-konvention:</b> <c>System.&lt;Event&gt;</c> (diskriminerar
/// från command-audit-events som <c>Application.Submitted</c>).
/// </para>
/// <para>
/// <b>AggregateType-konvention:</b> <c>System.&lt;Aggregate&gt;</c>
/// — compliance-koncept, ingen aggregate-root i Domain. Bevarar
/// Guid.Empty-invarianten via per-run-Guid (typiskt Hangfire jobId).
/// </para>
/// </summary>
public abstract record SystemAuditEvent(
    string EventType,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Audit-event för en avslutad JobAd-sync-run (stream eller snapshot).
/// </summary>
/// <param name="ParsedTotal">#510 — snapshot-körningens <c>SnapshotOutcome.ParsedTotal</c>
/// (SISTA attemptets element-antal). Detta är 7-dagars-baslinjens metrik
/// (<c>GetMaxObservedSnapshotSizeAsync</c> läser <c>MAX(payload->>'ParsedTotal')</c>) —
/// samma storhet som relativ-floorn jämför. <c>Fetched</c> duger INTE som baslinje:
/// den räknar yields över ALLA retry-attempts (pipeline-throughput, ADR 0045 klass
/// (d)) och inflateras av en trunkera-sen-lyckas-körning. Null för stream-rader och
/// legacy-rader (pre-#510) — NULL exkluderas ur MAX (uppvärmnings-semantik).</param>
public sealed record JobAdsSynced(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    string Source,
    string JobType,
    int Fetched,
    int Added,
    int Updated,
    int Archived,
    int Skipped,
    int Errors,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int? ParsedTotal = null)
    : SystemAuditEvent(
        EventType: "System.JobAdsSynced",
        AggregateType: "System.JobAdSync",
        AggregateId,
        OccurredAt);

/// <summary>
/// Audit-event för en raw_payload-purge-run (cron 30 4 * * *).
/// </summary>
public sealed record RawPayloadPurged(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    int RowsAffected,
    DateTimeOffset Cutoff,
    int RetentionDays)
    : SystemAuditEvent(
        EventType: "System.RawPayloadPurged",
        AggregateType: "System.RawPayloadPurge",
        AggregateId,
        OccurredAt);

/// <summary>
/// ADR 0032-amendment 2026-05-23 — audit-event för en retention-jobb-run.
/// Skrivs av <c>RetainPlatsbankenJobAdsJob</c> (Reason="snapshot-miss") och
/// <c>ExpireJobAdsJob</c> (Reason="expired"). Bulk-UPDATE-vägen kringgår
/// <c>JobAd.Archive()</c> och därmed <c>JobAdArchivedDomainEvent</c> per item
/// — denna aggregerade audit-rad är retention-vägens accountability-spår
/// (GDPR Art. 30; CTO-rond 2026-05-23 Q3=B).
/// </summary>
/// <param name="Reason">"snapshot-miss" eller "expired".</param>
/// <param name="ThresholdAborted">True om defense-in-depth-skydd avbröt arkivering (snapshot-trunkering, floor-violation eller post-archive circuit-breaker).</param>
/// <param name="AbortReason">Null om inte aborterat. "max-archive-pct-exceeded" om post-archive circuit-breaker triggade (CTO-rond 2026-05-23 H1).</param>
public sealed record JobAdsRetentionCompleted(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    string Source,
    string Reason,
    int ArchivedCount,
    int? Threshold,
    int? ParsedTotalLastSnapshot,
    int? Max7dObservedSnapshot,
    bool ThresholdAborted,
    string? AbortReason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
    : SystemAuditEvent(
        EventType: "System.JobAdsRetentionCompleted",
        AggregateType: "System.JobAdRetention",
        AggregateId,
        OccurredAt);

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 + C5) — audit-event för en fält-krypterings-
/// backfill-run. GDPR Art. 30 accountability: skrivs alltid (även 0 ägare
/// processade) — "behandlingsaktivitet har körts". Per-kolumn kvarvarande
/// legacy efter körningen = fitness-snapshot (ADR 0049 Validering).
/// </summary>
public sealed record FieldEncryptionBackfillRun(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    int OwnersProcessed,
    int OwnersFailed,
    long RemainingCoverLetter,
    long RemainingApplicationNoteContent,
    long RemainingFollowUpNote)
    : SystemAuditEvent(
        EventType: "System.FieldEncryptionBackfillRun",
        AggregateType: "System.FieldEncryptionBackfill",
        AggregateId,
        OccurredAt);
