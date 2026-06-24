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
/// </summary>
public sealed class GetMyMatchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetMyMatchesQuery, IReadOnlyList<MatchListItemDto>>
{
    // The view shows recent matches; the full set is reachable via the /jobb grade-filter.
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
