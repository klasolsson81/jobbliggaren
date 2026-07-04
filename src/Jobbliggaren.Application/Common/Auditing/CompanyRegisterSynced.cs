namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// #560 (ADR 0091) — audit-event for a completed SCB company-register refresh run (parity
/// <see cref="JobAdsSynced"/>). This is BOTH the GDPR Art. 30 accountability record for the
/// legal-entities-only bulk processing AND the completed-run watermark: the orchestrator reads the
/// most recent row's <c>TotalRowsFetched</c> as the relative-floor baseline for the deregister
/// sweep (parity <c>GetMaxObservedSnapshotSizeAsync</c> over <c>System.JobAdsSynced</c>). A dedicated
/// single-row sync-state table was deliberately NOT introduced (senior-cto-advisor 2026-07-04, Fork
/// 6 — YAGNI: no consumer needs it in this PR; the audit row suffices and stays consistent with the
/// existing system-job accountability pattern).
///
/// <para>
/// <b>No org.nr, no PII (CLAUDE.md §5):</b> the payload is aggregate counts only — the personnummer-
/// exclusion count proves the GDPR guard fired without ever naming a value.
/// </para>
/// </summary>
/// <param name="RowsExcludedPersonnummerShaped">Rows dropped by the defense-in-depth personnummer
/// guard before persistence — the audited proof the legal-entities-only invariant held.</param>
public sealed record CompanyRegisterSynced(
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    int RowsUpserted,
    int RowsDeregistered,
    int RowsExcludedPersonnummerShaped,
    int RowsExcludedInvalid,
    int TotalRowsFetched,
    bool SweepApplied,
    string? SweepSkipReason,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
    : SystemAuditEvent(
        EventType: "System.CompanyRegisterSynced",
        AggregateType: "System.CompanyRegisterSync",
        AggregateId,
        OccurredAt);
