namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #560 (ADR 0091) — one SCB category constraint in a <see cref="ScbQuery"/>, mapping to a
/// <c>{"Kategori": ..., "Kod": [...]}</c> element of the <c>raknaforetag</c>/<c>hamtaforetag</c>
/// request body (smoke-verified shape, issue #560). <paramref name="BranschNiva"/> is set only for
/// the <c>Bransch</c> category, which REQUIRES it — the deep SNI split filters at <c>BranschNiva</c> 3
/// (the 5-digit detaljgrupp), not the 2-digit division (that is its own <c>"2-siffrig bransch 1"</c>
/// category). #628 live-verified against SCB.
/// </summary>
internal sealed record ScbCategoryFilter(string Category, IReadOnlyList<string> Codes, int? BranschNiva = null);

/// <summary>
/// #560 (ADR 0091) — an immutable SCB filter: the set of category constraints AND'd together to
/// select a partition of the register. The count-then-slice planner starts from a seed (one
/// municipality × all legal Juridisk-form codes) and refines it down the facet ladder until each
/// partition is ≤ the SCB fetch cap. The population channel NEVER constrains on Företagsstatus — the
/// full mirror (incl. de-registered rows) is fetched and status is derived at ingest
/// (senior-cto-advisor 2026-07-04, Fork 4).
/// </summary>
internal sealed record ScbQuery(IReadOnlyList<ScbCategoryFilter> Filters)
{
    /// <summary>
    /// Returns a new query with the constraint on <paramref name="category"/> REPLACED by
    /// <paramref name="codes"/> (added if absent). Replacement is what makes the ladder work: the
    /// seed carries <c>Juridisk form = [all legal codes]</c>; a Juridisk-form split replaces it with
    /// a single code, narrowing the partition.
    /// </summary>
    public ScbQuery With(string category, IReadOnlyList<string> codes, int? branschNiva = null) =>
        new([.. Filters.Where(f => !string.Equals(f.Category, category, StringComparison.Ordinal)),
             new ScbCategoryFilter(category, codes, branschNiva)]);

    /// <summary>
    /// Returns a new query with the constraint on <paramref name="category"/> REMOVED (no-op if
    /// absent). Mirror of <see cref="With"/>; used by <see cref="ScbPrefixRung"/> to strip the 2-digit
    /// division constraint once its 5-digit child is applied (the 5-digit code implies the division, so
    /// keeping both would over-constrain the untested mixed-category shape).
    /// </summary>
    public ScbQuery Without(string category) =>
        new([.. Filters.Where(f => !string.Equals(f.Category, category, StringComparison.Ordinal))]);
}
