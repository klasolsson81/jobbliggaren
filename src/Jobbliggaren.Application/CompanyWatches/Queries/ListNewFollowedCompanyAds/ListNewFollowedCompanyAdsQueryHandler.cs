using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;

/// <summary>
/// #1576 — reads the ads the Översikt number counted. Owner-scoped; the set definition (watermark,
/// active watches, per-watch OnlyMatched fork, inclusion rule, ad collapse) is single-sourced in
/// <see cref="NewFollowedCompanyAdSet"/> so this list and that count cannot run two predicates.
///
/// <para>
/// <b>Lifecycle (ADR 0113):</b> this is a <c>JobAds</c> read site and its decision is Active-only —
/// inherited from the shared set's <c>j.Status == JobAdStatus.Active</c> gate, and stated here rather
/// than assumed because a surface that RENDERS ads owes the decision more than one that counts them.
/// Registered in <c>JobAdLifecycleReadRegistry</c>.
/// </para>
///
/// <para>
/// <b>Why a join and not an id-set.</b> The ad columns are projected by composing a join onto the
/// shared hit query. EF Core 10 + Npgsql cannot translate <c>Contains()</c> over the strongly-typed
/// <c>JobAdId</c> key (both the list form and the post-Select <c>.Value</c> form fail at RUNTIME, and
/// InMemory hides it — <c>JobAdEmployerReader</c> carries the measurement), and the <c>= ANY</c>
/// escape needs raw SQL, which is an Npgsql concern architecture-test-forbidden in Application. A
/// join has none of those problems and costs no extra round-trip.
/// </para>
/// </summary>
public sealed class ListNewFollowedCompanyAdsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch)
    : IQueryHandler<ListNewFollowedCompanyAdsQuery, NewFollowedCompanyAdsDto>
{
    /// <summary>
    /// The hard cap that satisfies §5's no-unbounded-fetch rule without pagination — the delivered
    /// <c>DisambiguateEmployersQuery.MaxResults</c> posture. Counted in HIT rows, which is the unit
    /// the acknowledgement window is expressed in; collapsing to ads can only shrink it.
    /// </summary>
    public const int MaxRows = 100;

    public async ValueTask<NewFollowedCompanyAdsDto> Handle(
        ListNewFollowedCompanyAdsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return NewFollowedCompanyAdsDto.Empty;

        var scope = await NewFollowedCompanyAdSet.LoadScopeAsync(db, userId, cancellationToken);
        if (scope is null)
            return NewFollowedCompanyAdsDto.Empty;

        // OLDEST first, plus one row to detect truncation without a second count query. Oldest-first
        // is not a preference: the acknowledgement window is the max over what we return, so taking
        // the oldest leaves everything newer above the watermark for the next visit. Taking the
        // newest would acknowledge past the rest and swallow it permanently.
        var window = await (
                from h in NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen)
                join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                orderby h.CreatedAt, h.JobAdId
                select new Row(
                    h.JobAdId,
                    h.CompanyWatchId,
                    h.CreatedAt,
                    new JobAdDto(
                        j.Id.Value,
                        j.Title,
                        j.Company.Name,
                        j.Url,
                        j.Source.Value,
                        j.Status.Value,
                        j.PublishedAt,
                        j.ExpiresAt,
                        j.CreatedAt)))
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var truncated = window.Count > MaxRows;
        if (truncated)
            window = window.GetRange(0, MaxRows);

        if (window.Count == 0)
            return NewFollowedCompanyAdsDto.Empty;

        // The window is acknowledged in the SCAN clock's unit, over every row read — including rows
        // the grade fork excludes below. Acknowledging only the included rows would hold the
        // watermark down behind an excluded older hit and re-show the same included ads every visit.
        var acknowledgedThrough = window.Max(r => r.HitCreatedAt);

        var hits = window
            .Select(r => new NewFollowedCompanyAdSet.Hit(r.JobAdId, r.CompanyWatchId, r.HitCreatedAt))
            .ToList();

        // Grade EVERY ad in the window, not only the OnlyMatched watches' ads: the inclusion rule
        // needs the latter, the per-row flag needs the former. One call, one SSOT, no second pass.
        var windowAdIds = hits.Select(h => h.JobAdId).Distinct().ToList();
        var resolution = await NewFollowedCompanyAdSet.ResolveMatchingAsync(
            profileBuilder, perUserSearch, windowAdIds, cancellationToken);

        var includedAdIds = NewFollowedCompanyAdSet.CollapseToAds(
            hits, scope.GradeWatchIds, resolution.Matching);

        // Per-ad ordering representative: an ad reached through two watches has two hit timestamps,
        // so "newest first" over ads is undefined without one. Max reads as "most recently brought to
        // my attention", which is what this surface claims to order by.
        var rows = window
            .Where(r => includedAdIds.Contains(r.JobAdId))
            .GroupBy(r => r.JobAdId)
            .Select(g => new
            {
                g.First().Ad,
                JobAdId = g.Key,
                Newest = g.Max(r => r.HitCreatedAt),
            })
            .OrderByDescending(x => x.Newest)
            .ThenBy(x => x.Ad.Id)
            .Select(x => new NewFollowedAdRow(
                x.Ad,
                resolution.Assessed ? resolution.Matching.Contains(x.JobAdId) : null))
            .ToList();

        return new NewFollowedCompanyAdsDto(rows, acknowledgedThrough, truncated);
    }

    /// <summary>One hit joined to its ad. Named because an anonymous type cannot cross the projection.</summary>
    private sealed record Row(
        JobAdId JobAdId, CompanyWatchId CompanyWatchId, DateTimeOffset HitCreatedAt, JobAdDto Ad);
}
