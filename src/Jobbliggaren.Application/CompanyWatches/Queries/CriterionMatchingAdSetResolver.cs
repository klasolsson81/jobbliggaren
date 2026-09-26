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
/// be mistaken for an authorization shortcut would eventually be used as one.
/// </para>
///
/// <para>
/// ⚠ <b>Since #1681 part 2 the criterion id is the DATABASE SELECTOR, not just a memo key</b>
/// (security-auditor, 2026-09-06). The three ad-side port methods read <c>members ⋈ job_ads</c> keyed
/// by that id, so passing an id the caller has not proven it owns is no longer a correctness bug — it
/// is a read of another user's materialised ad set. Every call site is owner-scoped today and
/// <see cref="CriterionToResolve"/> says so at the type; this paragraph exists so the next
/// contributor does not read the seam as safe on its own.
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

        // Read the memo BEFORE resolving, so the docblock's claim holds in both directions
        // (dotnet-architect, 2026-09-06). Without this the batch would re-measure a criterion the
        // single-criterion path had already answered and overwrite the memo with a SECOND
        // measurement — the two-resolutions-of-one-question defect this class exists against. Not
        // reachable through today's routes; an invariant with no coverage is still an invariant.
        var pending = new List<CriterionToResolve>(criteria.Count);
        foreach (var c in criteria)
        {
            if (_matching.TryGetValue(c.Id, out var memo))
                results[c.Id] = memo;
            else
                pending.Add(c);
        }

        if (pending.Count == 0)
            return results;

        // Assessability first, exactly as the single-criterion path does it: a caller who has stated
        // no occupation never pays for a scan whose result could not be graded, and the answer is a
        // nudge rather than a zero.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
        {
            foreach (var c in pending)
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
        foreach (var c in pending)
        {
            var (terminal, ids) = await ResolveIdsAsync(c.Id, c.Criteria, cancellationToken);
            if (terminal is not null)
                results[c.Id] = terminal;
            else
                idsByCriterion[c.Id] = ids!;
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

    /// <summary>
    /// The guard order and the refusal semantics, in ONE place — shared by
    /// <see cref="ResolveAsync"/> (one criterion) and <see cref="MatchingBatchAsync"/> (a list).
    ///
    /// <para>
    /// <b>It was extracted because the batch had copied it</b> (dotnet-architect, 2026-09-06). Two
    /// copies were equivalent the day they were written and are two copies the day after — and this
    /// class's own docblock justifies its existence by saying a second copy would drift, because the
    /// guard order, the refusal semantics and the grade authority are subtle enough that it would.
    /// Writing that argument and then duplicating the thing it protects is the vacuous-guarantee
    /// class this repo has already shipped twice (#805-3, #842).
    /// </para>
    ///
    /// <para>
    /// Returns a TERMINAL answer or the ad-id set, never both and never neither. The order of the
    /// guards is part of the contract and each one exists to avoid work the next would waste: the
    /// magnitude the surface needs anyway answers both refusals without a second round trip, so a
    /// refusal costs no id query of its own. <b>The gate never replaces the probe</b> — the port
    /// still asks for one row more than it can accept, so a set that grew between the count and the
    /// query is refused rather than truncated.
    /// </para>
    /// </summary>
    private async Task<(CriterionMatchingAds? Terminal, IReadOnlyList<JobAdId>? Ids)> ResolveIdsAsync(
        Guid criterionId, CompanyWatchCriteriaSpec criteria, CancellationToken cancellationToken)
    {
        var magnitude = await MagnitudeAsync(criterionId, criteria, cancellationToken);

        // Neither refusal is a zero, and they are NOT interchangeable: the breadth refusal is something the
        // user can act on by narrowing the watch, "not materialised" is something only the next run
        // fixes.
        if (magnitude.TooBroad)
            return (new CriterionMatchingAds.SetTooLarge(), null);
        if (magnitude.NotMaterialised)
            return (new CriterionMatchingAds.NotMaterialised(), null);
        if (magnitude.Magnitude > MaxSetSize)
            return (new CriterionMatchingAds.SetTooLarge(), null);

        var resolved = await browse.ListActiveAdIdsAsync(
            new CompanyWatchCriterionId(criterionId),
            CriteriaFingerprint.Of(criteria),
            MaxSetSize,
            cancellationToken);

        // The port refuses rather than truncating, so there is no prefix here to mistake for an
        // answer. Its non-answers map onto this hierarchy's own, one for one — and the two that read
        // as the breadth refusal stay distinct inside the port even though they render one sentence.
        if (resolved.Refused || resolved.State == CriterionMaterialisationState.TooBroad)
            return (new CriterionMatchingAds.SetTooLarge(), null);
        if (resolved.State == CriterionMaterialisationState.NotMaterialised)
            return (new CriterionMatchingAds.NotMaterialised(), null);

        return (null, resolved.Ids!);
    }

    private async Task<CriterionMatchingAds> ResolveAsync(
        Guid criterionId, CompanyWatchCriteriaSpec criteria, CancellationToken cancellationToken)
    {
        // Api-side, so BuildFullForSortAsync (ICurrentUser-scoped), never the Worker's
        // BuildFullForUserIdAsync — parity NewFollowedCompanyAdSet.ResolveMatchingAsync.
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return new CriterionMatchingAds.NotAssessed();

        var (terminal, ids) = await ResolveIdsAsync(criterionId, criteria, cancellationToken);
        if (terminal is not null)
            return terminal;

        // An empty set is a real answer (zero matching ads), NOT a refusal. Short-circuited because
        // grading an empty set would be a round-trip that cannot match a row.
        if (ids!.Count == 0)
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
/// The four answers this question has, as a CLOSED hierarchy — the private constructor means no
/// fifth kind can be declared elsewhere, and no combination of loose nullables can represent a
/// state that is not one of these four (§2.2, §5 primitive obsession).
///
/// <para>
/// <see cref="Resolved"/> with an empty list, <see cref="NotAssessed"/>, <see cref="SetTooLarge"/>
/// and <see cref="NotMaterialised"/> are four DIFFERENT things and a consumer must not collapse any
/// two: zero matches is a number, "you have stated no occupation" is a nudge, "this watch is too
/// broad to count" is a refusal, and "we have not counted this watch yet" is ignorance. Rendering
/// any of the latter three as "0" is the dishonest-zero trap this codebase names in every
/// neighbouring file.
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
    /// The two causes render the same sentence to a user, which is why
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
