using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Queries.RunSavedSearch;

public sealed class RunSavedSearchQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IJobAdSearchQuery search)
    : IQueryHandler<RunSavedSearchQuery, PagedResult<JobAdDto>?>
{
    public async ValueTask<PagedResult<JobAdDto>?> Handle(
        RunSavedSearchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var savedSearchId = new SavedSearchId(query.Id);

        var criteria = await db.SavedSearches
            .AsNoTracking()
            .Where(s => s.Id == savedSearchId && s.JobSeekerId == jobSeekerId)
            .Select(s => s.Criteria)
            .FirstOrDefaultAsync(cancellationToken);

        if (criteria is null)
        {
            // Failed-access-detection (ADR 0031): skilj okänt id från cross-tenant.
            var exists = await db.SavedSearches
                .AsNoTracking()
                .AnyAsync(s => s.Id == savedSearchId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "SavedSearch", savedSearchId.Value, currentUser.UserId.Value,
                    "RunSavedSearch");
            }
            return null;
        }

        // ADR 0039 Beslut 1 — samma sök-väg (IJobAdSearchQuery) som ListJobAds; VO→filter
        // via den delade SearchCriteriaMapping-SPOT:en (#312 — RunSavedSearch + den nya
        // GetNewSavedSearchResultsCount-räkningen kan aldrig divergera på vad ett sparat
        // filter betyder; #311 Employer + #551 Remote + Fas C2/B2-dimensionerna bor där).
        // ADR 0039 Beslut 2 — ingen last_run_at-skrivning (query, ej command). #293/#306:
        // det tidigare Since=null-argumentet utgår — "Ny" beräknas ur den per-sökning-
        // watermarken (SavedSearch.ResultsSeenAt), ej ett fast fönster.
        return await search.SearchAsync(
            new JobAdSearchCriteria(
                SearchCriteriaMapping.ToFilterCriteria(criteria),
                criteria.SortBy,
                query.Page,
                query.PageSize),
            cancellationToken);
    }
}
