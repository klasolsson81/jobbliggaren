using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #717 (ADR 0091) — unit tests for the pure over-cap tail arithmetic (ScbProtectedPartitionTails). The
/// total feeds #641's facet decision, so the multi-leaf case — the same (kommun, SNI) recorded by
/// several over-cap Juridisk-form leaves, each of which fetched its own first cap rows — is pinned
/// explicitly: tail = Σcount − cap × leaves, NOT Σcount − cap.
/// </summary>
public class ScbProtectedPartitionTailsTests
{
    private const int Cap = 2000;

    [Fact]
    public void Summarize_SingleOverCapLeaf_TailIsCountMinusCap()
    {
        var sizes = new Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize>
        {
            [new("0180", "70100")] = new(OverCapCount: 2809, LeafCount: 1),
        };

        var summary = ScbProtectedPartitionTails.Summarize(sizes, Cap);

        summary.TotalTailRows.ShouldBe(809);
        summary.Breakdown.ShouldBe("0180×70100:count=2809,leaves=1,tail=809");
    }

    [Fact]
    public void Summarize_MultipleLeavesSameKey_SubtractsCapPerLeaf()
    {
        // Two over-cap Juridisk forms under 0180×70100 accumulated to (5909, 2). Each fetched its own
        // first cap rows, so the tail is 5909 − 2000×2 = 1909 — NOT 5909 − 2000 (which over-counts to 3909).
        var sizes = new Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize>
        {
            [new("0180", "70100")] = new(OverCapCount: 5909, LeafCount: 2),
        };

        var summary = ScbProtectedPartitionTails.Summarize(sizes, Cap);

        summary.TotalTailRows.ShouldBe(1909);
        summary.Breakdown.ShouldBe("0180×70100:count=5909,leaves=2,tail=1909");
    }

    [Fact]
    public void Summarize_MultipleKeys_SumsTails_AndOrdersBiggestFirst()
    {
        var small = new ScbProtectedPartition("0180", "70200");   // tail 400
        var big = new ScbProtectedPartition("0180", "00000");     // tail 29000 (the Sthlm×AB×00000 shape)
        var sizes = new Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize>
        {
            [small] = new(OverCapCount: 2400, LeafCount: 1),
            [big] = new(OverCapCount: 31000, LeafCount: 1),
        };

        var summary = ScbProtectedPartitionTails.Summarize(sizes, Cap);

        summary.TotalTailRows.ShouldBe(29400);
        // Biggest tail first, so the densest cells #641 must capture read at a glance.
        summary.Breakdown.ShouldBe(
            "0180×00000:count=31000,leaves=1,tail=29000; 0180×70200:count=2400,leaves=1,tail=400");
    }

    [Fact]
    public void Summarize_EmptySet_ReturnsZeroAndEmptyBreakdown()
    {
        var summary = ScbProtectedPartitionTails.Summarize(
            new Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize>(), Cap);

        summary.TotalTailRows.ShouldBe(0);
        summary.Breakdown.ShouldBe(string.Empty);
    }

    [Fact]
    public void Summarize_ClampsDefensiveNonPositiveTailToZero()
    {
        // Defensive only: every recorded leaf is over-cap (count > cap) by construction, so a real tail is
        // always positive. If that invariant were ever violated the tail floors at 0, never negative.
        var sizes = new Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize>
        {
            [new("0180", "70100")] = new(OverCapCount: 3000, LeafCount: 2), // 3000 − 4000 < 0
        };

        var summary = ScbProtectedPartitionTails.Summarize(sizes, Cap);

        summary.TotalTailRows.ShouldBe(0);
        summary.Breakdown.ShouldBe("0180×70100:count=3000,leaves=2,tail=0");
    }

    [Fact]
    public void Summarize_CapsBreakdownAtTopN_ButTotalCoversEveryPartition()
    {
        // 65 protected partitions with distinct tails 1..65 (defensive parity with the #708 bounded log
        // fields — realistic cardinality is a few tens). The breakdown lists the top 60 biggest-first with
        // an overflow marker for the remaining 5; the TOTAL still sums every partition.
        var sizes = new Dictionary<ScbProtectedPartition, ScbProtectedPartitionSize>();
        for (var i = 1; i <= 65; i++)
            sizes[new ScbProtectedPartition("0180", $"{10000 + i}")] =
                new ScbProtectedPartitionSize(OverCapCount: Cap + i, LeafCount: 1); // tail = i

        var summary = ScbProtectedPartitionTails.Summarize(sizes, Cap);

        summary.TotalTailRows.ShouldBe(65 * 66 / 2);                          // 2145 — every partition, capped or not
        summary.Breakdown.ShouldEndWith("…(+5 fler skyddade partitioner, se totalen)");
        summary.Breakdown.ShouldContain("0180×10065:count=2065,leaves=1,tail=65"); // biggest tail listed first
        summary.Breakdown.Split(":count=").Length.ShouldBe(61);              // exactly 60 listed entries
    }
}
