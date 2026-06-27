using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatches;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — lists the authenticated user's background matches (most recent first,
/// capped) joined to each ad's PUBLIC details (title/company/url — no CV content). Owner-scoped;
/// no authenticated user → empty. The soft-delete query filter on <c>UserJobAdMatch</c> excludes
/// erased rows; the inner join to <c>JobAds</c> naturally drops a match whose ad is gone (a stale
/// link is never surfaced). <c>IsNew</c> is computed against the last-seen watermark as it stands
/// AT FETCH (opening the view advances it separately via MarkMatchesSeen). NO AI/LLM.
/// <para>
/// Deliberately status-AGNOSTIC: this surface does NOT filter on <c>NotificationStatus</c> —
/// a match is shown regardless of whether its notification is Pending/Queued/Sent/Failed, because
/// match VISIBILITY is independent of notification DELIVERY (TD-114). Do NOT add a status filter:
/// it would hide a real (e.g. email-Failed) match from the user.
/// </para>
/// </summary>
public sealed class GetMyMatchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetMyMatchesQuery, IReadOnlyList<MatchListItemDto>>
{
    // The view shows recent matches; the full set is reachable via the /jobb grade-filter.
    // #273 contract: this is a presentation/pagination cap on a recent-VIEW — NOT the new-match
    // cardinality. That truth lives in GetMyNewMatchCount, which is intentionally UNCAPPED and may
    // exceed this 50; the badge counts new matches, this list shows a window of them. Because rows
    // are ordered CreatedAt desc and "new" = CreatedAt > watermark, the visible IsNew rows equal
    // min(newCount, 50) — coherent below the cap, a documented bounded window above it. Do NOT
    // raise/remove this cap to "match the badge": the two surfaces measure different things.
    private const int MaxItems = 50;

    private static readonly IReadOnlyList<MatchListItemDto> Empty = [];

    public async ValueTask<IReadOnlyList<MatchListItemDto>> Handle(
        GetMyMatchesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Empty;

        var lastSeen = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.LastSeenMatchesAt)
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await (
                from m in db.UserJobAdMatches.AsNoTracking()
                where m.UserId == userId
                join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
                orderby m.CreatedAt descending, m.Id
                select new
                {
                    m.JobAdId,
                    j.Title,
                    Company = j.Company.Name,
                    j.Url,
                    m.Grade,
                    m.CreatedAt,
                })
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        // IsNew computed in-memory against the fetch-time watermark (null = never seen → all new).
        return rows
            .Select(r => new MatchListItemDto(
                r.JobAdId.Value, r.Title, r.Company, r.Url, r.Grade, r.CreatedAt,
                IsNew: lastSeen is not { } seen || r.CreatedAt > seen))
            .ToList();
    }
}
