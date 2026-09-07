using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.GetMyMatchingAdCountForCriterion;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatchCriteria;

/// <summary>
/// Owner-scoped list, newest first. No id is taken from the caller, so there is no IDOR surface
/// and no probe to log — an unauthenticated request yields the empty list's shape via the
/// fail-closed guard below (the endpoint is auth-gated anyway; this guard is what makes the
/// handler correct WITHOUT the front door, §2.4).
///
/// <para>
/// Materialize-then-map, deliberately: <c>Criteria</c> is the EF-ignored computed VO — it cannot
/// appear in a LINQ-to-entities projection, and the set is ≤ <c>MaxPerUser</c> (20) rows, so
/// mapping in memory is free.
/// </para>
///
/// <para>
/// <b>#1681 part 2 (ADR 0139) — each row now carries the SAME two ad numbers the detail page shows.</b>
/// Klas's requirement was not "a number" but *"exakta siffror, samma siffror som redan finns på smarta
/// bevakningar, och länkar så man kan se annonserna direkt"*, so the numbers are the detail page's own
/// DTOs (<see cref="CriterionAdMagnitudeDto"/>, <see cref="MyMatchingAdCountDto"/>) rather than new
/// members that would need their own honesty rules. One vocabulary, one set of states, two surfaces.
/// </para>
///
/// <para>
/// <b>This is the structural twin of <c>ListCompanyWatchesQueryHandler</c>, and after ADR 0139 that is
/// literally rather than merely formally true.</b> Both answer "my watches, with how many active ads
/// and how many match me" from a bounded org.nr set over public <c>job_ads</c> — the company block
/// from the org.nr the user followed, this one from the org.nr the materialisation job resolved. What
/// used to make them different — a 1,07M-row register join in the read path — is gone; the plan here
/// contains no <c>company_register</c> node at all (measured:
/// <c>docs/reviews/2026-09-06-1681-part2-read-form-measurement.md</c>, Result 3). That is what makes
/// <c>MeListRead</c> the right bucket for this route rather than the browse policy.
/// </para>
///
/// <para>
/// ⚠ <b>The conclusion is CONDITIONAL and the condition is the breadth gate</b> (ADR 0139,
/// "Läsvägens hem: <c>MeListRead</c>, villkorat"). It holds only while the member set stays bounded:
/// a criterion with ~1,07M members would make <c>members ⋈ job_ads</c> a large join again and this
/// bucket would under-protect exactly as before. The bucket decision and
/// <c>CompanyWatchCriterionMember.MaxPerCriterion</c> are therefore ONE decision, not two.
/// </para>
///
/// <para>
/// <b>The grading is batched into ONE call for the whole list</b> (senior-cto-advisor 2026-09-06,
/// binding), through <see cref="CriterionMatchingAdSetResolver.MatchingBatchAsync"/> — the same shape
/// the twin uses when it grades every watched employer in a single
/// <c>CountPerUserByEmployerAsync</c>. Resolving each criterion separately would have multiplied the
/// one round trip whose cost against the real grade predicate is the least well characterised.
/// </para>
/// </summary>
public sealed class ListCompanyWatchCriteriaQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    CriterionMatchingAdSetResolver resolver)
    : IQueryHandler<ListCompanyWatchCriteriaQuery, IReadOnlyList<CompanyWatchCriterionDto>>
{
    public async ValueTask<IReadOnlyList<CompanyWatchCriterionDto>> Handle(
        ListCompanyWatchCriteriaQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var userId = currentUser.UserId.Value;

        var criteria = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        if (criteria.Count == 0)
            return [];

        // Owner-scoped above, so every id below is one this request has already proven the caller
        // owns — which is the precondition the resolver's docblock sets for entering it at all.
        var matching = await resolver.MatchingBatchAsync(
            [.. criteria.Select(c => new CriterionToResolve(c.Id.Value, c.Criteria))],
            cancellationToken);

        var rows = new List<CompanyWatchCriterionDto>(criteria.Count);
        foreach (var c in criteria)
        {
            // Memoised by MatchingBatchAsync on the same per-request resolver, so this cannot become
            // a SECOND measurement of the same fact.
            //
            // It is NOT free on every path, and the unqualified claim that stood here was wrong
            // (code-reviewer + dotnet-architect, 2026-09-06): when the caller has stated no
            // occupation the batch returns at its assessability gate BEFORE measuring any magnitude,
            // so this loop pays up to MaxPerUser un-memoised CountActiveAdsAsync calls. That is the
            // same statement count the ordinary path pays and is inside the accepted trade-off — but
            // it is the common state early in onboarding, and the handler's own test
            // (ListCompanyWatchCriteriaQueryHandlerTests, the unassessable-profile case) measures it.
            var ads = await resolver.MagnitudeAsync(c.Id.Value, c.Criteria, cancellationToken);

            rows.Add(new CompanyWatchCriterionDto(
                c.Id.Value,
                c.Criteria.SniCodes,
                c.Criteria.MunicipalityCodes,
                c.Label,
                c.CreatedAt,
                c.UpdatedAt,
                ads,
                ToMatchingDto(matching[c.Id.Value])));
        }

        return rows;
    }

    /// <summary>
    /// The resolver's closed hierarchy onto the wire DTO, one arm at a time. The discard arm throws
    /// rather than returning a number, for the reason
    /// <c>GetMyMatchingAdCountForCriterionQueryHandler</c> gives at its own identical switch: every
    /// wrong answer here is a lie about how many jobs match the user, and a fifth kind cannot be
    /// declared outside <see cref="CriterionMatchingAds"/> anyway.
    /// </summary>
    private static MyMatchingAdCountDto ToMatchingDto(CriterionMatchingAds resolved) => resolved switch
    {
        CriterionMatchingAds.Resolved r => MyMatchingAdCountDto.Counted(r.Matching.Count),
        CriterionMatchingAds.NotAssessed => MyMatchingAdCountDto.NotAssessed,
        CriterionMatchingAds.SetTooLarge => MyMatchingAdCountDto.TooBroadToCount,
        CriterionMatchingAds.NotMaterialised => MyMatchingAdCountDto.NotMaterialisedYet,
        _ => throw new InvalidOperationException(
            $"Okänt CriterionMatchingAds-utfall: {resolved.GetType().Name}."),
    };
}
