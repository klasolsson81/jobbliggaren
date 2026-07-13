using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds;

/// <summary>
/// F6 P5 Punkt 2 Del A — listar aktuella bokmärken för inloggad JobSeeker.
/// ADR 0048 in-handler-join för JobAd-metadata via DefaultIfEmpty (ADR 0048
/// Beslut c — IgnoreQueryFilters/hand-rullade soft-delete-predikat FÖRBJUDET).
/// <para>
/// Den tidigare utsagan "soft-deletad JobAd → JobAd blir null via global query
/// filter" var falsk, och axeln den namngav finns inte längre: JobAd har ingen
/// soft-delete (#821). En annons som inte längre är aktiv bär
/// Status == "Archived" och joinar fortfarande;
/// JobAdSummaryDto.Status bär den signalen (aldrig null här — en sparad annons
/// har alltid en JobAd-rad).
/// </para>
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
                // #842 — an ERASED ad is projected as null, i.e. exactly like a missing one, so it
                // reuses the orphan row ("Annonsen är borttagen") that already exists below.
                //
                // This is the read path the erasure spec MISSED, and the miss is instructive: the
                // spec claimed "every other read path already filters Status == Active, so a fourth
                // status is excluded for free." That was true of search, matching, watches, suggest
                // and landing stats — and FALSE here, because #805-3 deliberately REMOVED the filter
                // so a saved ad that has been archived still renders. Without this guard an erased ad
                // renders as a normal card with an empty title and the company "[raderad]" — the
                // tombstone's own marker, on screen, to a user. A claim that a control covers "every
                // path" is worth exactly the enumeration behind it.
                r.j != null && r.j.Status != JobAdStatus.Erased
                    ? new JobAdSummaryDto(
                        r.j.Id.Value,
                        r.j.Title,
                        r.j.Company.Name,
                        r.j.Url,
                        r.j.Source.Value,
                        r.j.PublishedAt,
                        r.j.ExpiresAt,
                        // #805-3: Status projiceras (value-converter → string, samma
                        // idiom som Source). Sparade annonser har alltid en JobAd-rad
                        // — ingen ManualPosting-gren här, alltså aldrig null.
                        r.j.Status.Value)
                    : null))
            .ToListAsync(cancellationToken);

        return items;
    }
}
