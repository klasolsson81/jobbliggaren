using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// #1656 (b) — the single definition AND the single measurement of "the ads of the companies this
/// criterion matches THAT ALSO match ME (>= Good)", plus the ad magnitude that gates it.
///
/// <para>
/// Scoped, so both are measured at most ONCE per request however many handlers ask. Three consumers
/// share it: the criterion's ad magnitude, the personal count, and the filtered ad browse.
/// </para>
///
/// <para>
/// <b>Single-sourcing the definition was not enough, and the difference is a correctness one.</b>
/// Two resolutions of the same criterion are one authority producing TWO measurements, at two
/// instants. An ad published between them makes the headline and the list it links to disagree
/// inside a single response — on the one surface whose whole promise is that the number links to
/// exactly those ads. That is the #1407/#1471 divergence re-entering through the composition seam
/// rather than through a second predicate.
/// </para>
///
/// <para>
/// <b>The grade predicate is never written a second time.</b> It stays in <c>GradeRankExpression</c>
/// and is reached only through <see cref="IPerUserJobAdSearchQuery.FilterToMatchingAsync"/>, whose
/// ">= Good" floor is FIXED. The register half is reached only through
/// <see cref="ICompanyWatchBrowseQuery"/>.
/// </para>
///
/// <para>
/// <b>It never authorizes.</b> Every consumer loads the criterion owner-scoped itself and answers
/// 404 before reaching this type; the memo is per request and therefore per user. A memo that could
/// be mistaken for an authorization shortcut would eventually be used as one, so the criterion's
/// SPEC is what enters here — never an id the caller has not already proven it owns.
/// </para>
/// </summary>
public sealed class CriterionMatchingAdSetResolver(
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch,
    ICompanyWatchBrowseQuery browse)
{
    /// <summary>
    /// The most ads a criterion may have before the personal count is REFUSED rather than answered
    /// (senior-cto-advisor 2026-09-05, ADR 0120 clause 5).
    ///
    /// <para>
    /// <b>It bounds the INPUT, and the refusal is what makes the output exact.</b> A count over a
    /// truncated input is a floor, not a magnitude: grade a prefix and the number is simply false,
    /// while a "+" suffix reads as "approximately". So there is no saturating arm here and no
    /// <c>Saturated</c> flag anywhere downstream — at or below the bound the number is exact, above
    /// it there is no number at all.
    /// </para>
    ///
    /// <para>
    /// <b>Why this value.</b> It is the most rows the destination can ever page to
    /// (<see cref="CompanyBrowseCriteria.MaxPage"/> x the ads route's page size). Since the matching
    /// set is a SUBSET of this input, bounding the input here makes "the number links to exactly
    /// those ads, all of them" true BY CONSTRUCTION.
    /// </para>
    ///
    /// <para>
    /// Its OWN constant, never welded to <see cref="CompanyBrowseCriteria.MaxServableRows"/> or to
    /// <see cref="CriterionAdMagnitudeDto.Ceiling"/> (ADR 0120: the ceiling constant is reused as a
    /// pattern, never shared). Those answer "how far can this surface paginate" and "how far do we
    /// count ads"; this answers "how broad a watch will we grade at all".
    /// </para>
    /// </summary>
    public const int MaxSetSize = 2_000;

    private readonly Dictionary<Guid, CriterionAdMagnitudeDto> _magnitudes = [];
    private readonly Dictionary<Guid, CriterionMatchingAds> _matching = [];

    /// <summary>
    /// The criterion's ACTIVE-ad magnitude, saturating at
    /// <see cref="CriterionAdMagnitudeDto.Ceiling"/>.
    ///
    /// <para>
    /// <b>This is NOT the pagination count, and the two must not be merged.</b>
    /// <c>BrowseAdIdsAsync</c>'s internal total is capped at
    /// <see cref="CompanyBrowseCriteria.MaxServableRows"/> and answers "how far can this surface
    /// serve"; this one is capped at the product ceiling and answers "how many exist". Two
    /// questions, two ceilings — genuinely two measurements, correctly so.
    /// </para>
    /// </summary>
    public async Task<CriterionAdMagnitudeDto> MagnitudeAsync(
        Guid criterionId, CompanyWatchCriteriaSpec criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (_magnitudes.TryGetValue(criterionId, out var cached))
            return cached;

        var count = await browse.CountActiveAdsAsync(
            criteria, CriterionAdMagnitudeDto.Ceiling, cancellationToken);

        var magnitude = new CriterionAdMagnitudeDto(
            count, Saturated: count >= CriterionAdMagnitudeDto.Ceiling);
        _magnitudes[criterionId] = magnitude;
        return magnitude;
    }

    /// <summary>
    /// The criterion's matching ads, in the port's published order. The order of the three guards is
    /// part of the contract, and each one exists to avoid work the next would waste.
    ///
    /// <para>
    /// <b>Assessability first</b>, so a caller who has stated no occupation never pays for a scan
    /// whose result could not be graded. <b>Then the size gate</b>, from the magnitude this request
    /// measures for the headline anyway, so a refusal costs no register query of its own. <b>Then
    /// the probe.</b>
    /// </para>
    ///
    /// <para>
    /// <b>The gate never replaces the probe.</b> The port still asks for one row more than it can
    /// accept, so a set that grew between the count and the query is refused rather than truncated.
    /// The gate can only skip a query whose answer the count already determined; it can never admit
    /// one the probe would have refused.
    /// </para>
    /// </summary>
    public async Task<CriterionMatchingAds> MatchingAsync(
        Guid criterionId, CompanyWatchCriteriaSpec criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (_matching.TryGetValue(criterionId, out var cached))
            return cached;

        var resolved = await ResolveAsync(criterionId, criteria, cancellationToken);
        _matching[criterionId] = resolved;
        return resolved;
    }

    private async Task<CriterionMatchingAds> ResolveAsync(
        Guid criterionId, CompanyWatchCriteriaSpec criteria, CancellationToken cancellationToken)
    {
        // Api-side, so BuildFullForSortAsync (ICurrentUser-scoped), never the Worker's
        // BuildFullForUserIdAsync — parity NewFollowedCompanyAdSet.ResolveMatchingAsync.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return new CriterionMatchingAds.NotAssessed();

        var magnitude = await MagnitudeAsync(criterionId, criteria, cancellationToken);
        if (magnitude.Magnitude > MaxSetSize)
            return new CriterionMatchingAds.SetTooLarge();

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
        // as a signed Int32, and the two disagree on about half of all pairs.
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
public abstract record CriterionMatchingAds
{
    private CriterionMatchingAds() { }

    /// <summary>
    /// The matching ads, in the port's published order. The COUNT is this list's length — the number
    /// and its destination are the same value, which is what stops them diverging.
    /// </summary>
    public sealed record Resolved(IReadOnlyList<JobAdId> Matching) : CriterionMatchingAds;

    /// <summary>The user has stated no occupation, so matching is undefined — never zero.</summary>
    public sealed record NotAssessed : CriterionMatchingAds;

    /// <summary>
    /// The criterion's ad set exceeds <see cref="CriterionMatchingAdSetResolver.MaxSetSize"/>, so no
    /// honest number exists — never zero, and never a truncated count.
    /// </summary>
    public sealed record SetTooLarge : CriterionMatchingAds;
}
