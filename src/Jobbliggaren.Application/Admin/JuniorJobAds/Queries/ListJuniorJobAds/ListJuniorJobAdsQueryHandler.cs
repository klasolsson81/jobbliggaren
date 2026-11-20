using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;

/// <summary>
/// #2264 (ADR 0187) — the admin junior queue. One instant for the counts and the page. A narrow projection, never the
/// entity: the queue shows what the ad itself shows (title, employer, kommun, dates) and the rule's id, never its text.
/// </summary>
public sealed class ListJuniorJobAdsQueryHandler(
    IAppDbContext db,
    IJuniorFieldCatalog catalog,
    ITaxonomyReadModel taxonomy,
    IDateTimeProvider clock)
    : IQueryHandler<ListJuniorJobAdsQuery, JuniorJobAdQueueDto>
{
    public async ValueTask<JuniorJobAdQueueDto> Handle(ListJuniorJobAdsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var groups = JuniorMembership.GroupsOf(await catalog.ResolveEnabledAsync(cancellationToken)).ToList();

        // Every tab joins a listed ad in an enabled field to its assessment, so a closed or erased ad never appears. The
        // "Visas" tab composes JuniorMembership, the same definition /junior reads, so the two can never disagree.
        var listed = db.JobAds.AsNoTracking().Where(JobAdPublication.IsListed(now));
        var assessments = db.JuniorAssessments.AsNoTracking();

        var rows = listed
            .Where(JuniorMembership.InFields(groups))
            .Join(assessments, j => j.Id, a => a.Id, (j, a) => new { Ad = j, Assessment = a });

        var unclear = rows.Where(r => r.Assessment.Decision == null && r.Assessment.Verdict == JuniorVerdict.Unclear);
        var shown = listed
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups))
            .Join(assessments, j => j.Id, a => a.Id, (j, a) => new { Ad = j, Assessment = a });
        var decided = rows.Where(r => r.Assessment.Decision != null);

        var counts = new JuniorJobAdQueueCountsDto(
            await unclear.CountAsync(cancellationToken),
            await shown.CountAsync(cancellationToken),
            await decided.CountAsync(cancellationToken));

        var (source, total) = query.View switch
        {
            JuniorQueueView.Shown => (shown, counts.Shown),
            JuniorQueueView.Decided => (decided, counts.Decided),
            _ => (unclear, counts.Unclear),
        };

        var page = await source
            .OrderByDescending(r => r.Ad.PublishedAt)
            .ThenBy(r => r.Ad.Id)
            .Skip((query.Page - 1) * ListJuniorJobAdsQuery.PageSize)
            .Take(ListJuniorJobAdsQuery.PageSize)
            .Select(r => new
            {
                Id = r.Ad.Id.Value,
                r.Ad.Title,
                CompanyName = r.Ad.Company.Name,
                r.Ad.MunicipalityConceptId,
                r.Ad.PublishedAt,
                r.Ad.ExpiresAt,
                r.Assessment.Verdict,
                r.Assessment.RuleId,
                r.Assessment.Decision,
            })
            .ToListAsync(cancellationToken);

        var names = await PublicTaxonomyNames.ResolveAsync(
            taxonomy, page.Select(r => r.MunicipalityConceptId), cancellationToken);

        var items = page
            .Select(r => new JuniorJobAdQueueItemDto(
                r.Id, r.Title, r.CompanyName, PublicTaxonomyNames.NameOf(names, r.MunicipalityConceptId),
                r.PublishedAt, r.ExpiresAt, r.Verdict, r.RuleId, r.Decision))
            .ToList();

        return new JuniorJobAdQueueDto(
            new PagedResult<JuniorJobAdQueueItemDto>(items, total, query.Page, ListJuniorJobAdsQuery.PageSize),
            counts);
    }
}
