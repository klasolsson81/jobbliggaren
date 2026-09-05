using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// #1656 (b) — the single definition of "the ads of the companies this criterion matches THAT ALSO
/// match ME (>= Good)", shared by the two entry points that must never disagree:
/// <c>GetMyMatchingAdCountForCriterionQueryHandler</c> (the number on the criterion's detail page)
/// and <c>BrowseCriterionAdsQueryHandler</c>'s filtered arm (the destination that number links to).
///
/// <para>
/// Single-sourced for the reason <see cref="NewFollowedCompanyAdSet"/> is, and it is the same
/// sentence: a count and its set that run two predicates cannot be made to agree. This surface has
/// already shipped that defect twice under different names (#1407, #1471), and Klas's condition for
/// #1656 is that the number is "klickbart till exakt de annonserna" — so the number and the
/// destination are computed HERE, once, and neither handler computes any part of it itself.
/// </para>
///
/// <para>
/// <b>The grade predicate is never written a second time</b> (senior-cto-advisor's bind (iii),
/// 2026-09-04). It stays in <c>GradeRankExpression</c> and is reached only through
/// <see cref="IPerUserJobAdSearchQuery.FilterToMatchingAsync"/>, whose ">= Good" floor is FIXED —
/// no grade parameter and no numeric threshold to restate. The register half is reached only
/// through <see cref="ICompanyWatchBrowseQuery"/>. Neither half can express the other, which is
/// exactly why this composition lives in Application and the join does not.
/// </para>
/// </summary>
internal static class CriterionMatchingAdSet
{
    /// <summary>
    /// The most ads a criterion may have before this question is REFUSED rather than answered
    /// (senior-cto-advisor 2026-09-05, ADR 0120 clause 5).
    ///
    /// <para>
    /// <b>It bounds the INPUT, and the refusal is what makes the output exact.</b> A count over a
    /// truncated input is a floor, not a magnitude: grade a prefix and the number is simply false,
    /// while a "+" suffix reads as "approximately". So there is no saturating arm here and no
    /// <c>Saturated</c> flag anywhere downstream — below the bound the number is exact, above it
    /// there is no number at all.
    /// </para>
    ///
    /// <para>
    /// <b>Why this value.</b> It is the most rows the destination can ever page to
    /// (<see cref="CompanyBrowseCriteria.MaxPage"/> x the ads route's page size). Since the matching
    /// set is a SUBSET of this input, bounding the input here makes "the number links to exactly
    /// those ads, all of them" true BY CONSTRUCTION — a matching set the destination could not page
    /// to the end of would break Klas's condition in its weak form.
    /// </para>
    ///
    /// <para>
    /// Its OWN constant, never welded to <see cref="CompanyBrowseCriteria.MaxServableRows"/> or to
    /// <c>CriterionAdMagnitudeDto.Ceiling</c> (ADR 0120: the ceiling constant is reused as a
    /// pattern, never shared). Those answer "how far can this surface paginate" and "how far do we
    /// count ads"; this answers "how broad a watch will we grade at all". Call sites never restate
    /// it.
    /// </para>
    /// </summary>
    internal const int MaxSetSize = 2_000;

    /// <summary>
    /// Resolves the criterion's matching ads. The order of the two guards is part of the contract:
    /// <b>assessability is decided BEFORE the register is touched</b>, so a user who has stated no
    /// occupation never pays for a scan whose result could not be graded anyway.
    /// </summary>
    public static async Task<CriterionMatchingAds> ResolveAsync(
        IMatchProfileBuilder profileBuilder,
        IPerUserJobAdSearchQuery perUserSearch,
        ICompanyWatchBrowseQuery browse,
        CompanyWatchCriteriaSpec criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Api-side, so BuildFullForSortAsync (ICurrentUser-scoped), never the Worker's
        // BuildFullForUserIdAsync — parity NewFollowedCompanyAdSet.ResolveMatchingAsync.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return new CriterionMatchingAds.NotAssessed();

        var ids = await browse.ListActiveAdIdsAsync(criteria, MaxSetSize, cancellationToken);

        // null is "too broad to answer", never "nothing matched" — the port refuses rather than
        // truncating, so there is no prefix here to mistake for an answer.
        if (ids is null)
            return new CriterionMatchingAds.SetTooLarge();

        // An empty set is a real answer (zero matching ads), NOT a refusal. Short-circuited because
        // `= ANY('{}')` would be a round-trip that cannot match a row.
        if (ids.Count == 0)
            return new CriterionMatchingAds.Resolved([]);

        var matching = await perUserSearch.FilterToMatchingAsync(profile, ids, cancellationToken);

        // Filter the port's ORDERED list against the membership set — never enumerate the set. The
        // port's order is the contract (published_at DESC, id) and re-deriving it produces a
        // DIFFERENT one: Postgres compares uuid bytewise while Guid.CompareTo reads the first field
        // as a signed Int32, and the two disagree on about half of all pairs
        // (BrowseCriterionAdsQueryHandler records the same trap on the page path).
        return new CriterionMatchingAds.Resolved([.. ids.Where(matching.Contains)]);
    }
}

/// <summary>
/// The three answers this question has, as a CLOSED hierarchy — the private constructor means no
/// fourth kind can be declared elsewhere, and no combination of loose nullables can represent a
/// state that is not one of these three (§2.2, §5 primitive obsession).
///
/// <para>
/// <see cref="Resolved"/> with an empty list and <see cref="NotAssessed"/> and
/// <see cref="SetTooLarge"/> are three DIFFERENT things and a consumer must not collapse them: zero
/// matches is a number, "you have stated no occupation" is a nudge, and "this watch is too broad to
/// count" is a refusal. Rendering any of the latter two as "0" is the dishonest-zero trap this
/// codebase names in every neighbouring file.
/// </para>
/// </summary>
internal abstract record CriterionMatchingAds
{
    private CriterionMatchingAds() { }

    /// <summary>
    /// The matching ads, in the port's published order. The COUNT is this list's length — the number
    /// and its destination are the same value, which is what stops them diverging.
    /// </summary>
    internal sealed record Resolved(IReadOnlyList<JobAdId> Matching) : CriterionMatchingAds;

    /// <summary>The user has stated no occupation, so matching is undefined — never zero.</summary>
    internal sealed record NotAssessed : CriterionMatchingAds;

    /// <summary>
    /// The criterion's ad set exceeds <see cref="CriterionMatchingAdSet.MaxSetSize"/>, so no honest
    /// number exists — never zero, and never a truncated count.
    /// </summary>
    internal sealed record SetTooLarge : CriterionMatchingAds;
}
