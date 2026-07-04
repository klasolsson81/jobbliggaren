namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #560 (ADR 0091) — mutable run-outcome recorder for a full SCB population/refresh, written by
/// <see cref="IScbCompanyRegisterSource"/> while it counts+slices and read by the orchestrator
/// afterwards. Parity with <c>SnapshotOutcomeRecorder</c> in the JobTech snapshot job: the source
/// is the only component that knows whether the extract completed cleanly (every partition counted
/// and fetched) or was truncated/errored mid-run, and the orchestrator needs that verdict to decide
/// whether the deregister vanish-sweep may run.
///
/// <para>
/// <b>Why the truncation flag is load-bearing (parity the snapshot floor-guard):</b> the vanish-sweep
/// marks companies absent from a fresh extract as deregistered. A run that fetched only half the
/// municipalities (SCB 503 mid-run, a partition that could not be sliced ≤ the row cap, a cancelled
/// job) must NEVER let the sweep flip the untouched half to Deregistered. A truncated/errored outcome
/// skips the sweep entirely; a later clean run corrects.
/// </para>
///
/// <para>Not thread-safe by design — the source streams sequentially (at most one SCB call in flight
/// under the 10-calls/10-s budget), so no concurrent writes occur.</para>
/// </summary>
public sealed class ScbSyncOutcome
{
    private int _partitionsCounted;
    private int _partitionsFetched;
    private int _totalRowsFetched;
    private bool _truncatedOrErrored;

    /// <summary>Number of <c>raknaforetag</c> partitions counted this run.</summary>
    public int PartitionsCounted => _partitionsCounted;

    /// <summary>Number of <c>hamtaforetag</c> partitions fetched this run.</summary>
    public int PartitionsFetched => _partitionsFetched;

    /// <summary>Total rows fetched across all partitions — the relative-floor baseline for the sweep.</summary>
    public int TotalRowsFetched => _totalRowsFetched;

    /// <summary>
    /// True when the extract did not complete cleanly for a reason that is NOT itself an exception: a
    /// partition still exceeded the row cap after the facet ladder was exhausted, an empty code table,
    /// or an unrecognized SCB response envelope (fail-safe in the client). The orchestrator skips the
    /// deregister sweep when this is set. (A hard SCB HTTP error is handled differently — it throws out
    /// of <c>RefreshAsync</c> before the sweep is ever reached, so the same net invariant — never
    /// deregister on incomplete data — holds via both paths.)
    /// </summary>
    public bool TruncatedOrErrored => _truncatedOrErrored;

    /// <summary>Records that one partition was counted (before deciding to fetch or split it).</summary>
    public void RecordCounted() => _partitionsCounted++;

    /// <summary>Records that one partition was fetched, adding its row count to the run total.</summary>
    public void RecordFetched(int rows)
    {
        _partitionsFetched++;
        _totalRowsFetched += rows;
    }

    /// <summary>Latches the truncated/errored verdict (idempotent — once set, stays set).</summary>
    public void MarkTruncatedOrErrored() => _truncatedOrErrored = true;
}
