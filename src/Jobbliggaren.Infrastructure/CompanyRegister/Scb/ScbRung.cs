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
    /// #640 (Guard 2) — whether the planner must reconcile <c>sum(child counts)</c> against the parent
    /// count when it splits via this rung. True only where the child value set may not fully cover the
    /// parent (the 5-digit <c>Bransch</c> split — an entity with the parent's division but no listed
    /// 5-digit subcode is invisible to every child); a gap latches the run truncated. False for rungs
    /// whose children exhaust the parent by construction (single-valued Juridisk form), where
    /// reconciliation could only spuriously latch on mid-run SCB count drift.
    /// </summary>
    bool ReconcileChildren { get; }
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
    // #640: reconciliation is OFF by default for static rungs. The extension point exists so the 2-digit
    // division rung could opt in later (its children exhaust the parent only IF "2-siffrig bransch 1"
    // maps to a single membership slot — unconfirmed at #640, so left off; see #640 / ADR 0091 amendment).
    bool ReconcileChildren = false) : IScbRung
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
    // entity with the division but no listed 5-digit subcode), so the planner reconciles here.
    public bool ReconcileChildren => true;

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
