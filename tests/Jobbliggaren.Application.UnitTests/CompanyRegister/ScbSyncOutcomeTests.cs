using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #640 / #708 (ADR 0091) — unit tests for the run-outcome recorder's #640 additions (the
/// partition-scoped protected-key accumulator, Guard 1, and the no-SNI reconciliation-gap latch,
/// Guard 2) plus the #708 observability counters: the SCB-rejected partition-request tally (latching)
/// and the observe-only 2-digit reconciliation-gap tally (non-latching). The recorder is the
/// side-channel the client writes and the orchestrator reads to decide how the deregister sweep runs,
/// so its bookkeeping is pinned DB-free.
/// </summary>
public class ScbSyncOutcomeTests
{
    [Fact]
    public void RecordProtectedPartition_DeduplicatesKey_AndAccumulatesOverCapSize()
    {
        var outcome = new ScbSyncOutcome();

        // The SAME (kommun, SNI) is recorded by TWO over-cap leaves — e.g. two Juridisk forms under
        // 0180×70100: the ladder splits by form ABOVE the 5-digit SNI and the protected key drops the
        // form, so both over-cap leaves collapse to one key. A distinct SNI stays separate.
        outcome.RecordProtectedPartition("0180", "70100", 2809);
        outcome.RecordProtectedPartition("0180", "70100", 3100); // second over-cap leaf, same key
        outcome.RecordProtectedPartition("0180", "70200", 2400); // same kommun, different SNI → distinct

        // The sweep contract (the key set) still de-duplicates to the distinct (kommun, SNI) pairs.
        outcome.ProtectedPartitions.Count.ShouldBe(2);
        outcome.ProtectedPartitions.ShouldContain(new ScbProtectedPartition("0180", "70100"));
        outcome.ProtectedPartitions.ShouldContain(new ScbProtectedPartition("0180", "70200"));

        // #717 — the over-cap facts ACCUMULATE per key (counts summed, leaves tallied — never overwritten),
        // so the refresher can size the true tail Σcount − cap×leaves. Last-count-wins would under-count.
        outcome.ProtectedPartitionSizes[new ScbProtectedPartition("0180", "70100")]
            .ShouldBe(new ScbProtectedPartitionSize(OverCapCount: 5909, LeafCount: 2));
        outcome.ProtectedPartitionSizes[new ScbProtectedPartition("0180", "70200")]
            .ShouldBe(new ScbProtectedPartitionSize(OverCapCount: 2400, LeafCount: 1));
    }

    [Fact]
    public void RecordProtectedPartition_DoesNotLatchTruncated()
    {
        // Guard 1's whole point: a protected partition keeps the sweep ALIVE (scoped), it must not disable it.
        var outcome = new ScbSyncOutcome();

        outcome.RecordProtectedPartition("0180", "70100", 2809);

        outcome.TruncatedOrErrored.ShouldBeFalse();
        outcome.ReconciliationGaps.ShouldBe(0);
    }

    [Fact]
    public void RecordReconciliationGap_LatchesTruncated_AndCounts()
    {
        // Guard 2: a no-SNI completeness gap disables the WHOLE sweep (fail-closed) and is counted for the
        // distinct diagnostic log.
        var outcome = new ScbSyncOutcome();

        outcome.RecordReconciliationGap();
        outcome.RecordReconciliationGap();

        outcome.ReconciliationGaps.ShouldBe(2);
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public void RecordPartitionRequestFailed_LatchesTruncated_AndCounts()
    {
        // #708: a SCB-rejected partition request (rakna/hamta non-success) leaves its rows unfetched, so
        // it latches the run truncated (sweep disabled — never falsely deregister) AND is tallied for the
        // durable audit row (FailedPartitionCount).
        var outcome = new ScbSyncOutcome();

        outcome.RecordPartitionRequestFailed();
        outcome.RecordPartitionRequestFailed();

        outcome.PartitionRequestFailures.ShouldBe(2);
        outcome.TruncatedOrErrored.ShouldBeTrue();
        // Distinct from the no-SNI reconciliation counters — a partition rejection is its own failure class.
        outcome.ReconciliationGaps.ShouldBe(0);
        outcome.ObservedReconciliationGaps.ShouldBe(0);
    }

    [Fact]
    public void RecordObservedReconciliationGap_Counts_AndDoesNotLatchTruncated()
    {
        // #708: the 2-digit division rung reconciles OBSERVE-ONLY — a gap is tallied for the diagnostic
        // WARN (5716) but the run is NEVER latched truncated (observe-then-ratchet, CLAUDE.md §2.5). The
        // counter is independent of the latching reconciliation gap (Guard 2) and the partition-failure tally.
        var outcome = new ScbSyncOutcome();

        outcome.RecordObservedReconciliationGap();
        outcome.RecordObservedReconciliationGap();

        outcome.ObservedReconciliationGaps.ShouldBe(2);
        outcome.TruncatedOrErrored.ShouldBeFalse();      // observe-only → never disables the sweep
        outcome.ReconciliationGaps.ShouldBe(0);          // not the latching Guard-2 counter
        outcome.PartitionRequestFailures.ShouldBe(0);    // not a partition rejection
    }

    [Fact]
    public void MarkTruncatedOrErrored_Latches_WithoutIncrementingEitherNewCounter()
    {
        // The bare truncation latch must not be conflated with the #708 tallies: a run truncated for some
        // other reason (empty code table, envelope drift) still reports zero partition failures and zero
        // observed gaps — the audit counts stay attributable to their specific cause.
        var outcome = new ScbSyncOutcome();

        outcome.MarkTruncatedOrErrored();

        outcome.TruncatedOrErrored.ShouldBeTrue();
        outcome.PartitionRequestFailures.ShouldBe(0);
        outcome.ObservedReconciliationGaps.ShouldBe(0);
    }
}
