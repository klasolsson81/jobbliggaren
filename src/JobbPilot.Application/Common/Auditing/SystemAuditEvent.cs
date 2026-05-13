namespace JobbPilot.Application.Common.Auditing;

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
    DateTimeOffset CompletedAt)
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
