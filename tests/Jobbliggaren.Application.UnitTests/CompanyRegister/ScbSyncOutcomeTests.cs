using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #640 (ADR 0091) — unit tests for the run-outcome recorder's #640 additions: the partition-scoped
/// protected-key accumulator (Guard 1) and the no-SNI reconciliation-gap latch (Guard 2). The recorder
/// is the side-channel the client writes and the orchestrator reads to decide how the deregister sweep
/// runs, so its bookkeeping is pinned DB-free.
/// </summary>
public class ScbSyncOutcomeTests
{
    [Fact]
    public void RecordProtectedPartition_Deduplicates_SameKommunSniPair()
    {
        var outcome = new ScbSyncOutcome();

        outcome.RecordProtectedPartition("0180", "70100");
        outcome.RecordProtectedPartition("0180", "70100"); // same key from a re-counted partition
        outcome.RecordProtectedPartition("0180", "70200"); // same kommun, different SNI → distinct

        outcome.ProtectedPartitions.Count.ShouldBe(2);
        outcome.ProtectedPartitions.ShouldContain(new ScbProtectedPartition("0180", "70100"));
        outcome.ProtectedPartitions.ShouldContain(new ScbProtectedPartition("0180", "70200"));
    }

    [Fact]
    public void RecordProtectedPartition_DoesNotLatchTruncated()
    {
        // Guard 1's whole point: a protected partition keeps the sweep ALIVE (scoped), it must not disable it.
        var outcome = new ScbSyncOutcome();

        outcome.RecordProtectedPartition("0180", "70100");

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
}
