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

        var counted = await browse.CountActiveAdsAsync(
            new CompanyWatchCriterionId(criterionId),
            CriteriaFingerprint.Of(criteria),
            CriterionAdMagnitudeDto.Ceiling,
            cancellationToken);

        var magnitude = counted.State switch
        {
            CriterionMaterialisationState.Materialised =>
                CriterionAdMagnitudeDto.Counted(counted.Count!.Value, counted.Saturated),
            CriterionMaterialisationState.TooBroad => CriterionAdMagnitudeDto.TooBroadToCount,
            CriterionMaterialisationState.NotMaterialised =>
                CriterionAdMagnitudeDto.NotMaterialisedYet,
            _ => throw new InvalidOperationException(
                $"Okant materialiseringstillstand: {counted.State}."),
        };
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

    /// <summary>
    /// #1681 part 2 — the same question for a WHOLE LIST of criteria, resolved with ONE grading call
    /// instead of one per criterion (senior-cto-advisor 2026-09-06, binding).
    ///
    /// <para>
    /// <b>It lives here rather than in the list handler because otherwise there would be two homes for
    /// "how a criterion's matching ads are resolved".</b> That is precisely what this type exists to
    /// prevent: the guard order, the refusal semantics and the grade authority are subtle enough that
    /// a second copy would drift, and the surfaces would then disagree about the same watch — the
    /// #1407/#1471 class this class's own docblock is written against.
    /// </para>
    ///
    /// <para>
    /// <b>The grading is batched; the ad-id reads are not.</b> The twin handler
    /// (<c>ListCompanyWatchesQueryHandler</c>) grades in one <c>CountPerUserByEmployerAsync</c> call
    /// over the union of every watched org.nr, and this is the same shape one level along. The per-
    /// criterion statements stay per-criterion because that was the measured choice: batching the
    /// STATEMENT is dearer (a window function has to sort the whole join), while batching the GRADING
    /// is cheaper. Two different questions, both answered on measurement rather than on a preference
    /// for symmetry — <c>docs/reviews/2026-09-06-1681-part2-read-form-measurement.md</c>.
    /// </para>
    ///
    /// <para>
    /// Results are memoised into the same per-request maps the single-criterion methods use, so a
    /// later call for any of these criteria costs nothing and — more importantly — cannot produce a
    /// SECOND measurement of the same fact.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, CriterionMatchingAds>> MatchingBatchAsync(
        IReadOnlyList<CriterionToResolve> criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var results = new Dictionary<Guid, CriterionMatchingAds>();
        if (criteria.Count == 0)
            return results;

        // Assessability first, exactly as the single-criterion path does it: a caller who has stated
        // no occupation never pays for a scan whose result could not be graded, and the answer is a
        // nudge rather than a zero.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
        {
            foreach (var c in criteria)
            {
                var notAssessed = new CriterionMatchingAds.NotAssessed();
                _matching[c.Id] = notAssessed;
                results[c.Id] = notAssessed;
            }

            return results;
        }

        // Phase 1 — per criterion, resolve its ad-id set (or the reason there is none). Nothing is
        // graded yet, so a criterion that refuses costs no grading input at all.
        var idsByCriterion = new Dictionary<Guid, IReadOnlyList<JobAdId>>();
        foreach (var c in criteria)
        {
            var magnitude = await MagnitudeAsync(c.Id, c.Criteria, cancellationToken);

            if (magnitude.TooBroad)
            {
                results[c.Id] = new CriterionMatchingAds.SetTooLarge();
                continue;
            }

            if (magnitude.NotMaterialised)
            {
                results[c.Id] = new CriterionMatchingAds.NotMaterialised();
                continue;
            }

            if (magnitude.Magnitude > MaxSetSize)
            {
                results[c.Id] = new CriterionMatchingAds.SetTooLarge();
                continue;
            }

            var resolved = await browse.ListActiveAdIdsAsync(
                new CompanyWatchCriterionId(c.Id),
                CriteriaFingerprint.Of(c.Criteria),
                MaxSetSize,
                cancellationToken);

            if (resolved.Refused || resolved.State == CriterionMaterialisationState.TooBroad)
            {
                results[c.Id] = new CriterionMatchingAds.SetTooLarge();
                continue;
            }

            if (resolved.State == CriterionMaterialisationState.NotMaterialised)
            {
                results[c.Id] = new CriterionMatchingAds.NotMaterialised();
                continue;
            }

            idsByCriterion[c.Id] = resolved.Ids!;
        }

        // Phase 2 — ONE grading call over the UNION. FilterToMatchingAsync de-duplicates its input
        // itself, and criteria genuinely overlap (a user's watches tend to be neighbouring industries
        // or kommuner), so the union is usually smaller than the sum of its parts.
        var union = idsByCriterion.Values.SelectMany(static ids => ids).Distinct().ToList();
        var matching = union.Count == 0
            ? (IReadOnlySet<JobAdId>)new HashSet<JobAdId>()
            : await perUserSearch.FilterToMatchingAsync(profile, union, cancellationToken);

        // Phase 3 — attribute the graded set back per criterion. Filtering each criterion's OWN
        // ordered list against the membership set (never enumerating the set) keeps the port's
        // published order, for the reason the single-criterion path gives.
        foreach (var (id, ids) in idsByCriterion)
            results[id] = new CriterionMatchingAds.Resolved([.. ids.Where(matching.Contains)]);

        foreach (var (id, resolved) in results)
            _matching[id] = resolved;

        return results;
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

        // The magnitude already carries the criterion's materialisation state, so the two refusals it
        // can express are answered here without a second round trip. Neither is a zero, and they are
        // NOT interchangeable: "för bred" is something the user can act on by narrowing the watch,
        // "not materialised" is something only time (or the next run) fixes.
        if (magnitude.TooBroad)
            return new CriterionMatchingAds.SetTooLarge();
        if (magnitude.NotMaterialised)
            return new CriterionMatchingAds.NotMaterialised();
        if (magnitude.Magnitude > MaxSetSize)
            return new CriterionMatchingAds.SetTooLarge();

        var resolved = await browse.ListActiveAdIdsAsync(
            new CompanyWatchCriterionId(criterionId),
            CriteriaFingerprint.Of(criteria),
            MaxSetSize,
            cancellationToken);

        // The port refuses rather than truncating, so there is no prefix here to mistake for an
        // answer. Its three non-answers map onto this hierarchy's own three, one for one.
        if (resolved.Refused)
            return new CriterionMatchingAds.SetTooLarge();
        if (resolved.State == CriterionMaterialisationState.TooBroad)
            return new CriterionMatchingAds.SetTooLarge();
        if (resolved.State == CriterionMaterialisationState.NotMaterialised)
            return new CriterionMatchingAds.NotMaterialised();

        var ids = resolved.Ids!;

        // An empty set is a real answer (zero matching ads), NOT a refusal. Short-circuited because
        // grading an empty set would be a round-trip that cannot match a row.
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
    /// The criterion's ad set exceeds <see cref="CriterionMatchingAdSetResolver.MaxSetSize"/>, or its
    /// company set exceeded the breadth gate — either way it is too broad to answer, so no honest
    /// number exists. Never zero, and never a truncated count.
    ///
    /// <para>
    /// The two causes render the same sentence to a user ("bevakningen är för bred"), which is why
    /// they share an arm; they are kept apart INSIDE the port
    /// (<c>MaterialisedAdIds.Refused</c> vs its state) so neither can be inferred from the other
    /// there.
    /// </para>
    /// </summary>
    public sealed record SetTooLarge : CriterionMatchingAds;

    /// <summary>
    /// #1681 part 2 — the criterion's company set has not been materialised for its CURRENT predicate
    /// yet, so there was nothing to grade. Unknown, never zero, and deliberately NOT folded into
    /// <see cref="SetTooLarge"/>.
    ///
    /// <para>
    /// <b>Collapsing this into the refusal would be the dishonest zero one level up.</b> "Too broad"
    /// is a determinate answer the user can act on by narrowing the watch; this is ignorance that
    /// resolves itself on the next materialisation run. Telling a user to narrow a watch that is
    /// merely waiting to be counted is advice that cannot work.
    /// </para>
    /// </summary>
    public sealed record NotMaterialised : CriterionMatchingAds;
}

/// <summary>
/// #1681 part 2 — one criterion to resolve: the id the caller has ALREADY proven it owns, plus that
/// criterion's predicate. A pair rather than two parallel lists, because two lists that must stay
/// index-aligned are an argument-swap surface (CLAUDE.md 5, primitive obsession) — and here a swap
/// would silently attribute one watch's ads to another.
/// </summary>
public sealed record CriterionToResolve(Guid Id, CompanyWatchCriteriaSpec Criteria);
