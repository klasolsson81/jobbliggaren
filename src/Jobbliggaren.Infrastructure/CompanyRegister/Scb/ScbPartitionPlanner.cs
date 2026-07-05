using System.Runtime.CompilerServices;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #560 (ADR 0091) — one partition ready to fetch: a <see cref="ScbQuery"/> the planner has verified
/// (via a count) will return at most the SCB fetch cap of rows.
/// </summary>
internal sealed record ScbLeaf(ScbQuery Query, int Count);

/// <summary>
/// #560 (ADR 0091) — the adaptive count-then-slice partition planner. SCB caps <c>hamtaforetag</c>
/// at 2000 rows per call and has NO pagination, so the register can only be extracted by carving it
/// into partitions each provably ≤ the cap. This class is the PURE algorithm: given seed queries, a
/// rung ladder, the cap, and a <c>countAsync</c> delegate, it counts each partition (via
/// <c>raknaforetag</c>) BEFORE deciding to fetch it or split it further down the ladder, and yields
/// only cap-sized leaves. It performs no HTTP and no I/O itself — the delegate is injected and each
/// <see cref="IScbRung"/> already carries its data, so the slicing invariant is unit-testable with a
/// fake count function (no cert, no network).
///
/// <para>
/// <b>Ladder (#628):</b> SätesKommun (seed) → Juridisk form → 2-siffrig bransch (2-digit SNI division)
/// → Bransch niva 3 (5-digit SNI, fanned by the parent's 2-digit prefix — <see cref="ScbPrefixRung"/>).
/// Each rung only applies when the partition still exceeds the cap. If the deepest rung is exhausted —
/// or a rung reports it cannot split further (<see cref="IScbRung.Expand"/> returns empty) — and a
/// partition is STILL over the cap, the planner yields it anyway and latches
/// <see cref="ScbSyncOutcome.MarkTruncatedOrErrored"/> — the client fetches the first cap rows and
/// the run is marked incomplete, which (critically) DISABLES the deregister sweep so the missing
/// tail is never mistaken for de-registered companies.
/// </para>
/// </summary>
internal static class ScbPartitionPlanner
{
    /// <summary>
    /// Walks each seed down the ladder, counting before fetching, and yields cap-sized leaves.
    /// Records one <see cref="ScbSyncOutcome.RecordCounted"/> per <c>raknaforetag</c>. Zero-count
    /// partitions are skipped (no fetch). Depth-first via an explicit stack (bounded by ladder depth
    /// × max fan-out — no deep recursion).
    /// </summary>
    public static async IAsyncEnumerable<ScbLeaf> PlanAsync(
        IReadOnlyList<ScbQuery> seeds,
        IReadOnlyList<IScbRung> ladder,
        int maxRows,
        Func<ScbQuery, CancellationToken, Task<int>> countAsync,
        ScbSyncOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(countAsync);
        ArgumentNullException.ThrowIfNull(outcome);
        if (maxRows < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "maxRows måste vara >= 1.");

        // Depth = how many ladder rungs have already been applied to this query. Depth 0 = a seed.
        var stack = new Stack<(ScbQuery Query, int Depth)>();
        // Push in reverse so the first seed is processed first (cosmetic — order does not affect
        // correctness, only the deterministic call sequence the client tests assert against).
        for (var i = seeds.Count - 1; i >= 0; i--)
            stack.Push((seeds[i], 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (query, depth) = stack.Pop();

            var count = await countAsync(query, cancellationToken).ConfigureAwait(false);
            outcome.RecordCounted();

            if (count == 0)
                continue;

            if (count <= maxRows)
            {
                yield return new ScbLeaf(query, count);
                continue;
            }

            if (depth >= ladder.Count)
            {
                // Ladder exhausted, still over the cap. Fetch what we can (client caps at maxRows) but
                // mark the run truncated so the caller SKIPS the deregister sweep — a partition whose
                // tail we could not fetch must never look like de-registered companies.
                outcome.MarkTruncatedOrErrored();
                yield return new ScbLeaf(query, count);
                continue;
            }

            var children = ladder[depth].Expand(query);
            if (children.Count == 0)
            {
                // The rung cannot split this partition (e.g. a prefix rung with no children for the
                // parent's 2-digit code — defensive; the derived prefix map should always have one).
                // Same safe semantics as an exhausted ladder: fetch what we can, latch truncated so the
                // caller SKIPS the deregister sweep. Never let an over-cap partition vanish silently.
                outcome.MarkTruncatedOrErrored();
                yield return new ScbLeaf(query, count);
                continue;
            }

            // Fan out one child partition per rung child; re-count each before fetching (guarantees the
            // ≤ cap invariant at the leaf). Push in reverse for a stable, deterministic pop order.
            for (var i = children.Count - 1; i >= 0; i--)
                stack.Push((children[i], depth + 1));
        }
    }
}
