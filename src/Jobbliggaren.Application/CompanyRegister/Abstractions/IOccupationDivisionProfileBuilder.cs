namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #1682 — builds the derived occupation-group × SNI-division profile out of our own ads: for every
/// ssyk-level-4 occupation group, how many of its ads sit with an employer in each SNI huvudgrupp,
/// with the ads whose employer is not in the register kept as their own bucket rather than dropped.
/// The bransch picker reads it to answer a typed occupation word with where that occupation's
/// employers actually are — counted, never authored (tools/sni-aliases condition 5; #560 bind 4
/// stands: this is a measurement with its evidence attached, not a crosswalk).
///
/// <para>
/// Its own port, not a third method on <see cref="ICompanyWatchCriterionMaterialiser"/>: that port's
/// two methods answer "the register moved" and "a predicate moved"; this one answers a third actor,
/// "our own ad corpus moved". It resolves no user predicate and touches no criterion. It lives beside
/// the materialiser because the join against <c>company_register</c> is the thing that cannot be
/// done anywhere else (ADR 0139's placement rule): the register stays off <c>IAppDbContext</c>, so
/// the aggregate is computed inside Infrastructure and only counts cross this boundary.
/// </para>
/// <para>
/// The result is counts only — no rows, no org.nr — so the whole record is safe to log
/// (ADR 0087 D8(c)). "Not in register" and "in the register without an SNI code" are counted
/// separately: a bucket whose firings are not counted cannot be told from a bucket that never
/// filled, and the second one is expected to stay at zero.
/// </para>
/// </summary>
public interface IOccupationDivisionProfileBuilder
{
    /// <summary>
    /// Replaces the whole profile from the current ad corpus and register, in one transaction, and
    /// ANALYZEs the profile table once the rows are in (AGENTS.md §3.6). A disabled builder returns
    /// an all-zero result and logs that it was disabled; it never throws for being off.
    /// </summary>
    Task<OccupationDivisionProfileResult> BuildAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The tally of one completed build. Counts only. <see cref="AdsCounted"/> is the denominator every
/// share is later taken against, which is why it is carried here and on the run row rather than
/// re-derived by a reader.
/// </summary>
public sealed record OccupationDivisionProfileResult(
    int OccupationGroupsProfiled,
    int RowsWritten,
    int AdsCounted,
    int AdsNotInRegister,
    int AdsInRegisterWithoutSni,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
