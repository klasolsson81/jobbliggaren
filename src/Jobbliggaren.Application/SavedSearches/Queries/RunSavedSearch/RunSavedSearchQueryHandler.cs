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

        // ADR 0039 Beslut 1 — samma sök-väg (IJobAdSearchQuery) som ListJobAds.
        // ADR 0039 Beslut 2 — ingen last_run_at-skrivning (query, ej command).
        // ADR 0067 Fas C2: VO:t bär OccupationGroup + Municipality — mappas in
        // i filter-SPOT:en (C1:s tomma-listor-fönster täppt; sparade
        // yrkesgrupp-/kommun-sökningar filtrerar). Ssyk-dimensionen utgick med
        // reverse-lookup-migrationen (CTO-dom (e)/(f)). #293/#306: det tidigare
        // Since=null-argumentet utgår — "Ny" beräknas på FE (JobAdDto bär ingen
        // IsNew-flagga; SavedSearch exponerade ändå aldrig "Ny").
        return await search.SearchAsync(
            new JobAdSearchCriteria(
                new JobAdFilterCriteria(
                    OccupationGroup: criteria.OccupationGroup,
                    Municipality: criteria.Municipality,
                    Region: criteria.Region,
                    // ADR 0067 Beslut 6 (Fas B2) — VO:ts Klass 2 reproducerar filtret.
                    EmploymentType: criteria.EmploymentType,
                    WorktimeExtent: criteria.WorktimeExtent,
                    // #311 PR-2b C1 (ADR 0087 D6): PR-2:s CONTAINED-seam (Employer: []) ersatt —
                    // SearchCriteria-VO:t bär nu employer (org.nr) → en sparad sökning reproducerar
                    // sitt arbetsgivar-filter vid körning.
                    Employer: criteria.Employer,
                    // #551 PR-B: remote-persistensen deferrad (SearchCriteria bär ännu ingen
                    // remote-axel — följd-våg PR-D, parity #311 Employer-persistens PR-2b C1).
                    Remote: false,
                    Q: criteria.Q),
                criteria.SortBy,
                query.Page,
                query.PageSize),
            cancellationToken);
    }
}
