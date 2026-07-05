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
    // #640: keys the deregister sweep must SKIP. A HashSet de-duplicates a key the client may record more
    // than once; the class is sequential by design (see the type doc), so a plain HashSet is safe.
    private readonly HashSet<ScbProtectedPartition> _protectedPartitions = [];
    private int _partitionsCounted;
    private int _partitionsFetched;
    private int _totalRowsFetched;
    private int _reconciliationGaps;
    private bool _truncatedOrErrored;

    /// <summary>Number of <c>raknaforetag</c> partitions counted this run.</summary>
    public int PartitionsCounted => _partitionsCounted;

    /// <summary>Number of <c>hamtaforetag</c> partitions fetched this run.</summary>
    public int PartitionsFetched => _partitionsFetched;

    /// <summary>Total rows fetched across all partitions — the relative-floor baseline for the sweep.
    /// NB (#628): once partitions are split by SNI code, a company carrying several SNI codes
    /// (Bransch_1..5) can match several 5-digit <c>Bransch</c> partitions and be fetched more than once,
    /// so this total is ≥ the number of distinct rows persisted (the store upsert de-duplicates by
    /// org.nr). This is sound for the floor gate: the baseline compares against the max prior run under
    /// the SAME deterministic ladder (like-for-like), and any inflation can only make the extract look
    /// MORE complete — never cause a false deregistration (the truncation latch is the primary
    /// safeguard).</summary>
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

    /// <summary>
    /// #640 — the (SätesKommun, 5-digit SNI) partitions whose unfetched over-cap tail the deregister sweep
    /// must SKIP (the partition-scoped sweep). Empty on a run with no over-cap 5-digit tail, in which case
    /// the sweep runs unrestricted (#628 behaviour). Deduplicated. Read by the orchestrator after
    /// streaming and passed to <c>DeregisterMissingAsync</c>.
    /// </summary>
    public IReadOnlyCollection<ScbProtectedPartition> ProtectedPartitions => _protectedPartitions;

    /// <summary>
    /// #640 — how many times the no-SNI completeness reconciliation (Guard 2) detected
    /// <c>sum(child counts) &lt; parent count</c> at a 5-digit split. Non-zero implies
    /// <see cref="TruncatedOrErrored"/> is latched (the gap disables the whole sweep). Surfaced for a
    /// distinct diagnostic log — the binary latch alone collapses every reason to one string.
    /// </summary>
    public int ReconciliationGaps => _reconciliationGaps;

    /// <summary>Records that one partition was counted (before deciding to fetch or split it).</summary>
    public void RecordCounted() => _partitionsCounted++;

    /// <summary>Records that one partition was fetched, adding its row count to the run total.</summary>
    public void RecordFetched(int rows)
    {
        _partitionsFetched++;
        _totalRowsFetched += rows;
    }

    /// <summary>
    /// #640 — records that an over-cap 5-digit partition's tail must be excluded from the sweep. The
    /// sweep keeps running everywhere else (partition-scoped). Idempotent per key (deduplicated).
    /// </summary>
    public void RecordProtectedPartition(string seatMunicipalityCode, string sniCode) =>
        _protectedPartitions.Add(new ScbProtectedPartition(seatMunicipalityCode, sniCode));

    /// <summary>
    /// #640 (Guard 2) — records a no-SNI completeness gap (a 5-digit split whose child counts sum below
    /// the parent) and latches the run truncated so the sweep is disabled: an entity carrying a division
    /// but no listed 5-digit subcode is invisible to every child and must never be mistaken for a
    /// de-registered company. Kept distinct from a plain <see cref="MarkTruncatedOrErrored"/> only for
    /// diagnostics — the safety effect is identical.
    /// </summary>
    public void RecordReconciliationGap()
    {
        _reconciliationGaps++;
        _truncatedOrErrored = true;
    }

    /// <summary>Latches the truncated/errored verdict (idempotent — once set, stays set).</summary>
    public void MarkTruncatedOrErrored() => _truncatedOrErrored = true;
}
