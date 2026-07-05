namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #560 (ADR 0091) — the use-case port for a full SCB company-register refresh. ONE entry point
/// (<see cref="RefreshAsync"/>) invoked by the Worker's recurring Hangfire job (and, later, an admin
/// "trigger now" surface). The implementation is the Infrastructure orchestrator: it streams legal
/// entities from <see cref="IScbCompanyRegisterSource"/>, drops any personnummer-shaped org.nr
/// (defense-in-depth), bulk-upserts into <c>company_register</c>, runs the floor-gated deregister
/// sweep, and writes a <c>CompanyRegisterSynced</c> audit row.
///
/// <para>
/// The port lives in Application (Worker depends on Application; Application must not depend on
/// Worker) and returns a plain result DTO — the Hangfire wrapper stays a thin
/// <c>DisableConcurrentExecution</c> shell (Clean Arch, ADR 0023) that never sees the orchestration
/// mechanics.
/// </para>
/// </summary>
public interface IScbCompanyRegisterRefresher
{
    /// <summary>
    /// Runs one full refresh: fetch → filter → upsert → floor-gated deregister sweep → audit.
    /// Idempotent (upsert on org.nr; the sweep only flips status, never hard-deletes). Long-running
    /// (~1–3 h under the SCB throttle) — the caller guards against overlap via
    /// <c>DisableConcurrentExecution</c>.
    /// </summary>
    Task<ScbCompanyRegisterRefreshResult> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>
/// #560 (ADR 0091) — aggregate outcome of one refresh run, returned to the caller (admin surface /
/// integration tests) and mirrored into the <c>CompanyRegisterSynced</c> audit event.
/// </summary>
/// <param name="RowsUpserted">Rows inserted or updated in <c>company_register</c> this run.</param>
/// <param name="RowsDeregistered">Rows flipped to Deregistered by the vanish-sweep (0 when the sweep
/// was skipped).</param>
/// <param name="RowsExcludedPersonnummerShaped">Fetched rows dropped by the defense-in-depth
/// personnummer-shape guard before persistence (the GDPR guarantee — expected 0 when the SCB
/// Juridisk-form filter is correct, but always enforced).</param>
/// <param name="RowsExcludedInvalid">Fetched rows dropped because the org.nr failed 10-digit
/// validation.</param>
/// <param name="TotalRowsFetched">Total legal-entity rows fetched from SCB this run.</param>
/// <param name="SweepApplied">True when the deregister sweep ran; false when it was floor-gated off
/// (truncated/errored extract or below the safety floors).</param>
/// <param name="SweepSkipReason">Why the sweep was skipped, when <paramref name="SweepApplied"/> is
/// false; null otherwise.</param>
/// <param name="ProtectedPartitionCount">#640 — how many (kommun, SNI) partitions the sweep EXCLUDED
/// because their over-cap 5-digit tail could only be partially fetched (partition-scoped sweep). 0 on a
/// run with no dense-metro tail. The operational signal that Guard 1 engaged (and, if chronically
/// non-zero, that the ladder may need deepening later).</param>
/// <param name="StartedAt">Run start (from <c>IDateTimeProvider</c>).</param>
/// <param name="CompletedAt">Run completion (from <c>IDateTimeProvider</c>).</param>
public sealed record ScbCompanyRegisterRefreshResult(
    int RowsUpserted,
    int RowsDeregistered,
    int RowsExcludedPersonnummerShaped,
    int RowsExcludedInvalid,
    int TotalRowsFetched,
    bool SweepApplied,
    string? SweepSkipReason,
    int ProtectedPartitionCount,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
