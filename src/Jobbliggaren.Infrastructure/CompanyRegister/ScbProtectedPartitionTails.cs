using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #717 (ADR 0091) — pure tail sizing for the over-cap protected partitions. Given each protected
/// (kommun, SNI) key's accumulated over-cap facts (<see cref="ScbProtectedPartitionSize"/>) and the SCB
/// fetch cap, computes an UPPER BOUND on the total unfetched tail rows and a compact, counts-only
/// breakdown for the 5717 diagnostic. FREE instrumentation: the over-cap counts were already taken by
/// <c>raknaforetag</c>, so this adds ZERO SCB calls — it just reads what the run already knows. Sibling
/// of <c>ScbSweepGate</c>: a pure, unit-tested helper the refresher composes after streaming.
///
/// <para>
/// <b>Upper bound, not an exact shortfall.</b> A legal entity carrying several SNI codes (Bransch_1..5)
/// matches — and is counted by <c>raknaforetag</c> in — several 5-digit cells, so summing per-cell tails
/// can count the same unfetched company more than once (and a company fetched via a NON-over-cap sibling
/// cell still adds to an over-cap cell's tail). The aggregate is therefore "at most N rows short" — the
/// same double-count caveat <see cref="ScbSyncOutcome.TotalRowsFetched"/> documents (#628). It is a
/// sizing signal for #641's facet choice, not a precise deficit; the log carries it with <c>≈</c> and an
/// explicit "övre gräns" (upper bound) marker.
/// </para>
///
/// <para>
/// The cap-subtraction lives HERE in Infrastructure (the SCB fetch cap is an Infrastructure concept —
/// <c>ScbRegisterOptions.BatchSize</c>), never in the Application <see cref="ScbSyncOutcome"/>. Kept a
/// pure static so the multi-leaf arithmetic is unit-tested DB-free — for a key recorded by <c>N</c>
/// over-cap leaves (one per over-cap Juridisk form; the key drops the form), each leaf fetched its own
/// first <c>cap</c> rows, so that key's tail is <c>Σcount − cap × N = OverCapCount − cap × LeafCount</c>
/// (exact for the cell; the cross-cell sum is the upper bound above). The breakdown carries kommun + SNI
/// + count + tail only — public administrative taxonomy codes and aggregate counts, never an org.nr or
/// personnummer (CLAUDE.md §5).
/// </para>
/// </summary>
internal static class ScbProtectedPartitionTails
{
    // Defensive bound on the breakdown line's length (parity the #708 log-field bounds — DescribeQuery
    // MaxDescriptorLength / ReadReasonAsync MaxReasonLength). The realistic protected-partition count is a
    // few tens (dense-metro over-cap cells bounded by taxonomy cardinality), so this never bites in
    // practice; the aggregate TotalTailRows is ALWAYS summed over every partition, capped or not.
    private const int MaxBreakdownPartitions = 60;

    /// <summary>
    /// Sums the (upper-bound) unfetched tail rows across all protected partitions and renders a compact
    /// breakdown (biggest tail first, then kommun/SNI for a stable order, so the densest cells #641 must
    /// capture read at a glance). The breakdown lists at most <see cref="MaxBreakdownPartitions"/> entries
    /// with an overflow marker; the total always covers every partition. An empty input yields
    /// <c>(0, "")</c> — the caller guards on a non-empty protected set before emitting.
    /// </summary>
    public static ScbTailSummary Summarize(
        IReadOnlyDictionary<ScbProtectedPartition, ScbProtectedPartitionSize> sizes, int cap)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        var ordered = sizes
            .OrderByDescending(kv => TailRows(kv.Value, cap))
            .ThenBy(kv => kv.Key.SeatMunicipalityCode, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.SniCode, StringComparer.Ordinal)
            .ToList();

        var total = 0;
        var parts = new List<string>(Math.Min(ordered.Count, MaxBreakdownPartitions));
        foreach (var (key, size) in ordered)
        {
            var tail = TailRows(size, cap);
            total += tail; // summed over ALL partitions, even any past the breakdown cap
            if (parts.Count < MaxBreakdownPartitions)
                parts.Add(
                    $"{key.SeatMunicipalityCode}×{key.SniCode}:count={size.OverCapCount},leaves={size.LeafCount},tail={tail}");
        }

        var breakdown = string.Join("; ", parts);
        if (ordered.Count > MaxBreakdownPartitions)
            breakdown += $"; …(+{ordered.Count - MaxBreakdownPartitions} fler skyddade partitioner, se totalen)";

        return new ScbTailSummary(total, breakdown);
    }

    // Each of the key's LeafCount over-cap leaves fetched its own first cap rows; Math.Max clamps the
    // defensive impossible-negative (every recorded leaf is over-cap, so count > cap per leaf).
    private static int TailRows(ScbProtectedPartitionSize size, int cap) =>
        Math.Max(0, size.OverCapCount - cap * size.LeafCount);
}

/// <summary>
/// #717 — the result of <see cref="ScbProtectedPartitionTails.Summarize"/>: the (upper-bound) total
/// unfetched tail rows across all protected partitions and a compact counts-only breakdown for the 5717
/// log line.
/// </summary>
internal readonly record struct ScbTailSummary(int TotalTailRows, string Breakdown);
