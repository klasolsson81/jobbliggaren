namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #628 (ADR 0091) — one rung of the partition facet ladder. Given a parent partition the planner has
/// found is still over the SCB fetch cap, a rung produces the child partitions to try next (each
/// re-counted before fetching). A rung is a PURE strategy: every input it needs (code lists, the
/// 2→5-digit SNI prefix map) is captured at construction by the client BEFORE planning, so the planner
/// stays free of HTTP/IO and the ladder is unit-testable with plain data.
///
/// <para>
/// Replaces the v1 static <c>ScbFacet</c>: the deep SNI split needs a rung whose child values depend on
/// the parent partition (the 5-digit <c>Bransch</c> rung fans only the codes under the parent's chosen
/// 2-digit division — see <see cref="ScbPrefixRung"/>), which a fixed value list cannot express.
/// </para>
/// </summary>
internal interface IScbRung
{
    /// <summary>
    /// Produces the child partitions to split <paramref name="parent"/> into. An empty result means
    /// "cannot split further" — the planner yields the partition as an over-cap leaf and the client
    /// decides whether it can be bounded to a protected (kommun, SNI) key or must latch the run truncated.
    /// </summary>
    IReadOnlyList<ScbQuery> Expand(ScbQuery parent);

    /// <summary>
    /// #640 (Guard 2) / #708 — how the planner reconciles <c>sum(child counts)</c> against the parent
    /// count when it splits via this rung. <see cref="ScbReconciliationMode.Latch"/> where the child
    /// value set may not fully cover the parent (the 5-digit <c>Bransch</c> split — an entity with the
    /// parent's division but no listed 5-digit subcode is invisible to every child); a gap latches the
    /// run truncated. <see cref="ScbReconciliationMode.Observe"/> counts a gap diagnostically WITHOUT
    /// latching (#708: the 2-digit division rung — observe-only until an explicit ratchet; the
    /// comparison is free since the eager child counts already happen). <see cref="ScbReconciliationMode.Off"/>
    /// for rungs whose children exhaust the parent by construction (single-valued Juridisk form), where
    /// reconciliation could only spuriously fire on mid-run SCB count drift.
    /// </summary>
    ScbReconciliationMode ReconciliationMode { get; }
}

/// <summary>
/// #708 — reconciliation posture for a rung's <c>sum(child counts) vs parent count</c> check (#640
/// Guard 2). Tri-state so a new guard can ship observe-only and be promoted to latching on live
/// evidence (CLAUDE.md §2.5 observe-then-ratchet; ADR 0091 amendment 2026-07-06): a latching guard
/// whose firing behavior has never been observed must not gate a costly ~11 h population run.
/// </summary>
internal enum ScbReconciliationMode
{
    /// <summary>No reconciliation — the rung's children exhaust the parent by construction.</summary>
    Off,

    /// <summary>Count a gap diagnostically (surfaced in the run log), but never latch truncation.</summary>
    Observe,

    /// <summary>A gap latches the run truncated — the deregister sweep is disabled (#640 Guard 2).</summary>
    Latch,
}

/// <summary>
/// #628 (ADR 0091) — a rung that fans a FIXED set of values, one child per value, replacing the
/// constraint on <paramref name="Category"/>. Used for the parent-independent rungs: Juridisk form and
/// the 2-digit SNI division (<c>"2-siffrig bransch 1"</c>).
/// </summary>
internal sealed record ScbStaticRung(
    string Category,
    IReadOnlyList<string> Values,
    int? BranschNiva = null,
    // #640/#708: Off by default for static rungs (single-valued Juridisk form exhausts the parent by
    // construction). The 2-digit division rung opts in at Observe (#708): the 2026-07-05 population
    // proved the derived 2-digit set is live (division "00" cells fetched at baseline in mid-size
    // municipalities), so its coverage is a real testable property — but the guard ships observe-only
    // until a completion run shows its firing behavior (promotion to Latch is an explicit ratchet).
    ScbReconciliationMode ReconciliationMode = ScbReconciliationMode.Off) : IScbRung
{
    public IReadOnlyList<ScbQuery> Expand(ScbQuery parent) =>
        [.. Values.Select(value => parent.With(Category, [value], BranschNiva))];
}

/// <summary>
/// #628 (ADR 0091) — the DYNAMIC 5-digit <c>Bransch</c> rung: its children depend on the parent's chosen
/// 2-digit code. It reads the parent's <paramref name="ParentCategory"/> (<c>"2-siffrig bransch 1"</c>)
/// constraint and fans only the 5-digit codes under that division (from <paramref name="PrefixMap"/>),
/// not all ~800 codes — a prefix-filtered child facet that keeps the count fan-out to ~10 per over-cap
/// 2-digit partition. Each child DROPS the now-subsumed 2-digit constraint (the 5-digit code implies its
/// division) so the emitted query is the pure 5-digit shape verified live against SCB
/// (<c>{"Kategori":"Bransch","BranschNiva":3,"Kod":["70100"]}</c>).
/// </summary>
internal sealed record ScbPrefixRung(
    string ParentCategory,
    string ChildCategory,
    int ChildBranschNiva,
    IReadOnlyDictionary<string, IReadOnlyList<string>> PrefixMap) : IScbRung
{
    // #640 (Guard 2) — the 5-digit split is the one point where children may not cover the parent (an
    // entity with the division but no listed 5-digit subcode), so the planner latches here (shipped
    // latching at #640; unchanged by the #708 tri-state).
    public ScbReconciliationMode ReconciliationMode => ScbReconciliationMode.Latch;

    public IReadOnlyList<ScbQuery> Expand(ScbQuery parent)
    {
        // The parent must carry exactly one 2-digit code on ParentCategory; anything else is a
        // malformed partition we refuse to split (planner then latches truncated — fail-safe).
        var codes = parent.Filters
            .FirstOrDefault(filter => string.Equals(filter.Category, ParentCategory, StringComparison.Ordinal))
            ?.Codes;
        if (codes is not { Count: 1 }
            || !PrefixMap.TryGetValue(codes[0], out var children)
            || children.Count == 0)
        {
            return [];
        }

        var stripped = parent.Without(ParentCategory);
        return [.. children.Select(code => stripped.With(ChildCategory, [code], ChildBranschNiva))];
    }
}
