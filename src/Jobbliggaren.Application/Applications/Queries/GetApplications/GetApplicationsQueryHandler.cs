using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetApplications;

public sealed class GetApplicationsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetApplicationsQuery, PagedResult<ApplicationDto>>
{
    public async ValueTask<PagedResult<ApplicationDto>> Handle(
        GetApplicationsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Empty(query);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Empty(query);

        var baseQuery = db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId);

        if (query.Status is not null &&
            ApplicationStatus.TryFromName(query.Status, out var status))
        {
            baseQuery = baseQuery.Where(a => a.Status == status);
        }

        // Separat count-query per CLAUDE.md §3.6 — projection-fri count är effektivare
        // än materialisering + Count() och låter EF generera SELECT COUNT(*).
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // ADR 0048: EN LEFT JOIN job_ads via GroupJoin/DefaultIfEmpty FÖRE
        // materialisering. JobAd:s globala query-filter (DeletedAt == null)
        // ärvs automatiskt → soft-deletad JobAd ger j == null → fallback.
        // IgnoreQueryFilters / manuellt DeletedAt-predikat FÖRBJUDET (ADR 0048 c).
        var items = await baseQuery
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                x.a,
                j,
                // Väg (D): härled FK-Guid ur joinade JobAd (j) — undviker
                // Nullable<JobAdId>.Value-unwrap i uttrycksträdet (InMemory-
                // brott). Soft-deletad JobAd → j == null → JobAdGuid null
                // (önskat, ADR 0048 — FK ej mot rad användaren ej får se).
                JobAdGuid = j != null ? (Guid?)j.Id.Value : null
            })
            .Select(r => new ApplicationDto(
                r.a.Id.Value,
                r.a.JobSeekerId.Value,
                r.JobAdGuid,
                r.a.Status.Name,
                r.a.CreatedAt,
                r.a.UpdatedAt,
                r.j != null
                    ? new JobAdSummaryDto(
                        r.j.Id.Value, r.j.Title, r.j.Company.Name, r.j.Url,
                        r.j.Source.Value, r.j.PublishedAt, r.j.ExpiresAt)
                    : r.a.ManualPosting != null
                        ? new JobAdSummaryDto(
                            null, r.a.ManualPosting.Title, r.a.ManualPosting.Company,
                            r.a.ManualPosting.Url, "Manual",
                            (DateTimeOffset?)null, r.a.ManualPosting.ExpiresAt)
                        : null,
                r.a.AppliedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ApplicationDto>(items, totalCount, query.Page, query.PageSize);
    }

    private static PagedResult<ApplicationDto> Empty(GetApplicationsQuery query) =>
        new(Array.Empty<ApplicationDto>(), 0, query.Page, query.PageSize);
}
