using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds;

/// <summary>
/// F6 P5 Punkt 2 Del A — listar aktuella bokmärken för inloggad JobSeeker.
/// ADR 0048 in-handler-join för JobAd-metadata; soft-deletad JobAd → JobAd
/// blir null via global query filter + DefaultIfEmpty (ADR 0048 Beslut c —
/// IgnoreQueryFilters/manuellt DeletedAt-predikat FÖRBJUDET).
/// </summary>
public sealed class ListSavedJobAdsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<ListSavedJobAdsQuery, IReadOnlyList<SavedJobAdDto>>
{
    public async ValueTask<IReadOnlyList<SavedJobAdDto>> Handle(
        ListSavedJobAdsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        var items = await db.SavedJobAds
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId)
            .OrderByDescending(s => s.CreatedAt)
            .GroupJoin(db.JobAds, s => s.JobAdId, j => j.Id, (s, ja) => new { s, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new { x.s, j })
            .Select(r => new SavedJobAdDto(
                r.s.Id.Value,
                r.s.JobAdId.Value,
                r.s.CreatedAt,
                r.j != null
                    ? new JobAdSummaryDto(
                        r.j.Id.Value,
                        r.j.Title,
                        r.j.Company.Name,
                        r.j.Url,
                        r.j.Source.Value,
                        r.j.PublishedAt,
                        r.j.ExpiresAt)
                    : null))
            .ToListAsync(cancellationToken);

        return items;
    }
}
