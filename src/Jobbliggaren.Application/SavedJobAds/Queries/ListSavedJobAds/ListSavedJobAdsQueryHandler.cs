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
                // Without it, the tombstone renders as a normal card: empty title, company
                // "[raderad]".
                //
                // `!= Erased`, NOT `== Active`: #805-3 deliberately REMOVED the Active filter here so
                // a saved ad that has since been ARCHIVED still renders. Re-adding it would re-kill
                // that fix. (The gated/ungated read paths are tabled in ADR 0106 §D9, not enumerated
                // in a comment.)
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
