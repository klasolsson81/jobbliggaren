using System.Diagnostics;
using System.Runtime.CompilerServices;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #560 (ADR 0091) — one partition ready to fetch: a <see cref="ScbQuery"/> the planner has verified
/// (via a count) will return at most the SCB fetch cap of rows — UNLESS <paramref name="OverCap"/> is
/// set. #640: <paramref name="OverCap"/> marks a partition the ladder could not slice below the cap
/// (its <paramref name="Count"/> exceeds the cap). The client fetches only the first cap rows and
/// decides whether the tail can be bounded to a protected (kommun, SNI) key (partition-scoped sweep) or
/// the run must latch truncated — the planner is category-agnostic and never makes that call.
/// #712: <paramref name="Depth"/> is how many ladder rungs were already applied to reach this leaf, so a
/// fetch-failed leaf can re-enter the planner as a retry seed through its REMAINING ladder
/// (<c>ladder[Depth..]</c>) — re-splitting correctly if its count has drifted over the cap.
/// </summary>
internal sealed record ScbLeaf(ScbQuery Query, int Count, bool OverCap = false, int Depth = 0);

/// <summary>
/// #712 — the result of a partition count (<c>raknaforetag</c>): either a successful count or a failure
/// (SCB rejected the request). Replaces the bare <c>int</c> so a count-failed partition is distinguishable
/// from a legitimate zero — the planner skips a zero (nothing to fetch) but must re-queue a failure for
/// the end-of-run retry wave instead of silently dropping it (and its ladder children).
/// </summary>
internal readonly record struct ScbCountResult(bool Succeeded, int Count)
{
    public static ScbCountResult Ok(int count) => new(true, count);
    public static ScbCountResult Failure { get; } = new(false, 0);
}

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
/// partition is STILL over the cap, the planner yields it as an OVER-CAP leaf
/// (<see cref="ScbLeaf.OverCap"/>). #640 moved the truncate-or-protect decision OUT of the planner: the
/// client (which owns the SCB category semantics) decides whether the over-cap tail can be bounded to a
/// protected (kommun, SNI) key (partition-scoped sweep) or the run must latch truncated. The planner
/// stays purely about slicing; it touches <paramref name="outcome"/> only to count nodes and, at a
/// reconciling rung, to record a completeness gap (Guard 2 — latching or observe-only per the rung's
/// <see cref="ScbReconciliationMode"/>, #708).
/// </para>
/// </summary>
internal static class ScbPartitionPlanner
{
    /// <summary>
    /// Walks each seed down the ladder, counting before fetching, and yields cap-sized leaves (plus
    /// over-cap leaves the ladder could not slice — flagged <see cref="ScbLeaf.OverCap"/>). Records one
    /// <see cref="ScbSyncOutcome.RecordCounted"/> per node (a node counted while reconciling its parent's
    /// split carries that count and is not re-counted). Zero-count partitions are skipped (no fetch).
    /// Depth-first via an explicit stack (bounded by ladder depth × max fan-out — no deep recursion).
    /// </summary>
    public static async IAsyncEnumerable<ScbLeaf> PlanAsync(
        IReadOnlyList<ScbQuery> seeds,
        IReadOnlyList<IScbRung> ladder,
        int maxRows,
        Func<ScbQuery, CancellationToken, Task<ScbCountResult>> countAsync,
        ScbSyncOutcome outcome,
        Action<ScbQuery, int> onCountFailed,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(countAsync);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(onCountFailed);
        if (maxRows < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "maxRows måste vara >= 1.");

        // Stack item: the query, how many ladder rungs already applied (depth 0 = a seed), and — for a
        // node whose count was already taken while reconciling its parent's split (Guard 2, eager child
        // count) — that known count so the node is never re-counted. Push in reverse so the first seed is
        // processed first (cosmetic — order does not affect correctness).
        var stack = new Stack<(ScbQuery Query, int Depth, int? KnownCount)>();
        for (var i = seeds.Count - 1; i >= 0; i--)
            stack.Push((seeds[i], 0, null));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (query, depth, knownCount) = stack.Pop();

            // Count exactly once per node: a reconciled child already carries its count.
            int count;
            if (knownCount is { } known)
            {
                count = known;
            }
            else
            {
                // A top-level count is only ever taken for a seed (children are pushed WITH their known
                // count). #712: a count failure here re-queues the seed for the end-of-run retry wave (via
                // onCountFailed) instead of dropping it as a phantom zero — depth is where its remaining
                // ladder resumes.
                var result = await countAsync(query, cancellationToken).ConfigureAwait(false);
                outcome.RecordCounted();
                if (!result.Succeeded)
                {
                    onCountFailed(query, depth);
                    continue;
                }
                count = result.Count;
            }

            if (count == 0)
                continue;

            if (count <= maxRows)
            {
                yield return new ScbLeaf(query, count, Depth: depth);
                continue;
            }

            // Over cap and either the ladder is exhausted or the next rung cannot split this partition.
            // Yield it as an OVER-CAP leaf and let the client decide protect-vs-latch (#640) — the planner
            // is category-agnostic and never bounds a (kommun, SNI) key itself.
            if (depth >= ladder.Count)
            {
                yield return new ScbLeaf(query, count, OverCap: true, Depth: depth);
                continue;
            }

            var rung = ladder[depth];
            var children = rung.Expand(query);
            if (children.Count == 0)
            {
                yield return new ScbLeaf(query, count, OverCap: true, Depth: depth);
                continue;
            }

            // Guard 2 (#640) — eager child count: count every direct child NOW (recording each) so the sum
            // of child counts can be reconciled against this parent BEFORE recursing. Each surviving child
            // is pushed WITH its known count, so it is fetched-or-split on pop without a second count
            // (RecordCounted stays exactly once per node). Zero-count children are counted into the sum but
            // never pushed (nothing to fetch).
            var childrenWithCounts = new List<(ScbQuery Query, int Count)>(children.Count);
            var failedChildren = new List<ScbQuery>();
            var childSum = 0;
            foreach (var child in children)
            {
                var childResult = await countAsync(child, cancellationToken).ConfigureAwait(false);
                outcome.RecordCounted();
                if (!childResult.Succeeded)
                {
                    // #712: a count-failed child is captured for the retry wave — how depends on the
                    // rung's reconciliation posture (below), because a failed child makes childSum
                    // incomplete and the completeness check must never fire on partial data.
                    failedChildren.Add(child);
                    continue;
                }
                childSum += childResult.Count;
                if (childResult.Count > 0)
                    childrenWithCounts.Add((child, childResult.Count));
            }

            // #712: a failed child count leaves childSum incomplete, so the completeness reconciliation
            // (below) cannot run on this parent this pass. How the failure is captured for the retry wave
            // depends on the rung's reconciliation posture:
            if (failedChildren.Count > 0)
            {
                if (rung.ReconciliationMode == ScbReconciliationMode.Latch)
                {
                    // SAFETY (the load-bearing case): the 5-digit Bransch split HARD-LATCHES a no-subcode
                    // completeness gap (Guard 2 — an entity carrying the division but no listed 5-digit
                    // subcode is invisible to every child and must never be mistaken for a de-registered
                    // company). Re-queue the WHOLE PARENT (its own query + depth), NOT the individual failed
                    // child, so the wave re-counts EVERY child and re-runs this reconciliation — otherwise a
                    // genuine gap here would be permanently masked by a transient sibling failure that later
                    // recovers (residual → 0 → non-truncated → the sweep falsely deregisters the no-subcode
                    // entities). The surviving children are not pushed; the parent re-drive covers them
                    // (idempotent upsert, so a delay, not a correctness issue).
                    onCountFailed(query, depth);
                    continue;
                }

                // Observe / Off: masking a gap here has NO safety consequence (Observe is diagnostic-only —
                // it never latches truncation; Off never reconciles), so re-queue the failed children
                // individually (through their own remaining ladder at depth+1 — cheaper, tiny blast radius)
                // and push the survivors normally. Reconciliation is skipped below (childSum incomplete).
                foreach (var failedChild in failedChildren)
                    onCountFailed(failedChild, depth + 1);
            }

            // Completeness reconciliation (#640 Guard 2 / #708 tri-state): a child sum below the parent
            // means an entity matches the parent but no listed child code — invisible to every child.
            // Latch mode (the 5-digit Bransch split) latches the run truncated so the sweep is disabled
            // (a steady-state refresh must never mistake such an entity for a de-registered company).
            // Observe mode (#708: the 2-digit division rung) only counts the gap for the diagnostic log —
            // a new guard ships observe-only until a completion run shows its firing behavior. NB: both
            // UNDER-detect — an entity double-counted across several child codes inflates the sum and can
            // mask a real gap; the truncation latch + floors remain the primary safeguards.
            // #712: gated on a COMPLETE childSum (no failed child) — a Latch parent with a failed child
            // took the parent-requeue path above (reconciliation deferred to the wave); an Observe/Off
            // parent with a failed child must not reconcile on a partial sum (a false observed gap / no-op).
            if (failedChildren.Count == 0 && childSum < count)
            {
                switch (rung.ReconciliationMode)
                {
                    case ScbReconciliationMode.Latch:
                        outcome.RecordReconciliationGap();
                        break;
                    case ScbReconciliationMode.Observe:
                        outcome.RecordObservedReconciliationGap();
                        break;
                    case ScbReconciliationMode.Off:
                        break;
                    default:
                        // Fail fast on a future enum value instead of a silent no-op (dotnet-architect #708).
                        throw new UnreachableException($"Ohanterat ScbReconciliationMode: {rung.ReconciliationMode}.");
                }
            }

            for (var i = childrenWithCounts.Count - 1; i >= 0; i--)
                stack.Push((childrenWithCounts[i].Query, depth + 1, childrenWithCounts[i].Count));
        }
    }
}
