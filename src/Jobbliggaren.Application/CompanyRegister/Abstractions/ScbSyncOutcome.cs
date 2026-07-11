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
    // #640: keys the deregister sweep must SKIP → #717: mapped to their accumulated over-cap size so the
    // run can size each tail for free. A Dictionary keyed by the identity VO de-duplicates the sweep key
    // AND accumulates the count/leaf tallies a key may receive more than once (the same (kommun, SNI)
    // recurs across Juridisk-form branches — see ScbProtectedPartitionSize). The class is sequential by
    // design (see the type doc), so a plain Dictionary is safe.
    private readonly Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize> _protectedPartitions = [];
    private int _partitionsCounted;
    private int _partitionsFetched;
    private int _totalRowsFetched;
    private int _reconciliationGaps;
    private int _observedReconciliationGaps;
    private int _partitionRequestFailures;
    private bool _hardLatched;

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
    /// True when the extract did not complete cleanly. DERIVED (#712) from two orthogonal causes:
    /// a monotonic HARD latch (an unbounded over-cap leaf, an empty code table, an unrecognized SCB
    /// response envelope, or a no-SNI reconciliation gap — none retryable) OR a non-zero
    /// <see cref="PartitionRequestFailures"/> tally (SCB-rejected partition requests — RETRYABLE by the
    /// #712 end-of-run wave). A run with a fully-recovered retry wave sets the residual tally to 0, which
    /// clears the partition-failure cause; any hard latch still forces truncation. The orchestrator skips
    /// the deregister sweep whenever this is set. (A hard SCB HTTP error is handled differently — it throws
    /// out of <c>RefreshAsync</c> before the sweep is ever reached, so the same net invariant — never
    /// deregister on incomplete data — holds via both paths.)
    /// </summary>
    public bool TruncatedOrErrored => _hardLatched || _partitionRequestFailures > 0;

    /// <summary>
    /// #640 — the (SätesKommun, 5-digit SNI) partitions whose unfetched over-cap tail the deregister sweep
    /// must SKIP (the partition-scoped sweep). Empty on a run with no over-cap 5-digit tail, in which case
    /// the sweep runs unrestricted (#628 behaviour). Deduplicated. Read by the orchestrator after
    /// streaming and passed to <c>DeregisterMissingAsync</c>. (The key set is unchanged by #717 — the
    /// over-cap sizes hang off the values, never the sweep key.)
    /// </summary>
    public IReadOnlyCollection<ScbProtectedPartition> ProtectedPartitions => _protectedPartitions.Keys;

    /// <summary>
    /// #717 — each protected partition mapped to its accumulated over-cap size
    /// (<see cref="ScbProtectedPartitionSize"/>), so the orchestrator can size each unfetched tail for
    /// free (the <c>raknaforetag</c> counts were already taken). Read after streaming to emit the tail
    /// diagnostic (5717) — pure #641 facet evidence; touches neither the sweep nor the audit payload.
    /// </summary>
    public IReadOnlyDictionary<ScbProtectedPartition, ScbProtectedPartitionSize> ProtectedPartitionSizes =>
        _protectedPartitions;

    /// <summary>
    /// #640 — how many times the no-SNI completeness reconciliation (Guard 2) detected
    /// <c>sum(child counts) &lt; parent count</c> at a 5-digit split. Non-zero implies
    /// <see cref="TruncatedOrErrored"/> is latched (the gap disables the whole sweep). Surfaced for a
    /// distinct diagnostic log — the binary latch alone collapses every reason to one string.
    /// </summary>
    public int ReconciliationGaps => _reconciliationGaps;

    /// <summary>
    /// #708 — how many times an OBSERVE-mode reconciliation (the client's <c>Observe</c> rung mode,
    /// today the 2-digit division rung) detected <c>sum(child counts) &lt; parent count</c>. Diagnostic
    /// ONLY — does NOT latch <see cref="TruncatedOrErrored"/> (observe-only until an explicit ratchet,
    /// CLAUDE.md §2.5 / ADR 0091 amendment 2026-07-06). Non-zero means a whole 2-digit division's
    /// companies were invisible to the split — evidence for whether the rung should be promoted to
    /// latching.
    /// </summary>
    public int ObservedReconciliationGaps => _observedReconciliationGaps;

    /// <summary>
    /// #708/#712 — the count of SCB-rejected partition requests (<c>raknaforetag</c>/<c>hamtaforetag</c>
    /// non-success). During the main stream each rejection increments this tally (which alone latches
    /// <see cref="TruncatedOrErrored"/> — a run with no retry wave behaves exactly as before #712). After
    /// the #712 end-of-run retry wave, <see cref="SetResidualPartitionFailures"/> RESETS it to the residual
    /// — the number of originally-failed partitions still unrecovered (0 if all recovered). Surfaced in the
    /// durable audit payload (<c>FailedPartitionCount</c>) so a truncated run is diagnosable from the audit
    /// row alone — 40 unattributed 400s on the 2026-07-05 population run motivated this counter. Kodtabell
    /// (dimension-table) failures are NOT counted here — they abort seeding/laddering and are logged
    /// separately.
    /// </summary>
    public int PartitionRequestFailures => _partitionRequestFailures;

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
    /// sweep keeps running everywhere else (partition-scoped). Deduplicated by (kommun, SNI) key.
    /// #717 — ACCUMULATES the over-cap facts: the same key can be recorded by several over-cap leaves
    /// (one per over-cap Juridisk form under this (kommun, SNI) — the ladder splits by form above SNI and
    /// the key drops it), so sum the counts and tally the leaves rather than overwrite. Each leaf fetched
    /// its own first cap rows, so the true tail is <c>Σcount − cap × leaves</c>
    /// (see <see cref="ScbProtectedPartitionSize"/>). Never receives an org.nr (CLAUDE.md §5).
    /// </summary>
    public void RecordProtectedPartition(string seatMunicipalityCode, string sniCode, int overCapCount)
    {
        var key = new ScbProtectedPartition(seatMunicipalityCode, sniCode);
        var current = _protectedPartitions.GetValueOrDefault(key);
        _protectedPartitions[key] = new ScbProtectedPartitionSize(
            current.OverCapCount + overCapCount, current.LeafCount + 1);
    }

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
        _hardLatched = true;
    }

    /// <summary>
    /// #708 — records an OBSERVE-mode reconciliation gap (the 2-digit division rung): counted for the
    /// diagnostic log, but the run is NOT latched truncated. The mode is deliberately observe-only on
    /// first shipment (a latching guard whose firing behavior has never been observed must not gate a
    /// costly ~11 h run) — promotion to latching is an explicit follow-up ratchet once a completion run
    /// has produced the evidence.
    /// </summary>
    public void RecordObservedReconciliationGap() => _observedReconciliationGaps++;

    /// <summary>
    /// #708/#712 — records one SCB-rejected partition request (<c>raknaforetag</c>/<c>hamtaforetag</c>
    /// non-success) by incrementing the failure tally. A non-zero tally alone latches
    /// <see cref="TruncatedOrErrored"/> (a rejected query's rows are unfetched, so the sweep must not treat
    /// them as vanished), so a run WITHOUT a #712 retry wave behaves exactly as before. It does NOT set the
    /// monotonic hard latch — the #712 end-of-run wave may recover the partition, and
    /// <see cref="SetResidualPartitionFailures"/> then resets this tally to the unrecovered residual. Kept
    /// distinct from <see cref="MarkTruncatedOrErrored"/> so the audit row can carry the failure count
    /// (parity <see cref="RecordReconciliationGap"/>).
    /// </summary>
    public void RecordPartitionRequestFailed() => _partitionRequestFailures++;

    /// <summary>
    /// #712 — after the end-of-run retry wave, RESETS the partition-failure tally to the residual: the
    /// number of originally-failed partitions still unrecovered. A residual of 0 clears the
    /// partition-failure cause of <see cref="TruncatedOrErrored"/> so a fully-recovered run runs the sweep
    /// (any hard latch from another cause still forces truncation). Idempotent per call — the wave calls it
    /// exactly once after draining. Never negative.
    /// </summary>
    public void SetResidualPartitionFailures(int residual)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(residual);
        _partitionRequestFailures = residual;
    }

    /// <summary>
    /// #712 — latches the monotonic HARD truncation verdict (idempotent — once set, stays set). Used for
    /// the non-retryable causes: an unbounded over-cap leaf, an empty code table, or an unrecognized SCB
    /// response envelope. A hard latch is never cleared by the retry wave.
    /// </summary>
    public void MarkTruncatedOrErrored() => _hardLatched = true;
}
