using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.SavedSearches.Queries;
using JobbPilot.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.SavedSearches.Queries.GetSavedSearch;

public sealed class GetSavedSearchQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetSavedSearchQuery, SavedSearchDto?>
{
    public async ValueTask<SavedSearchDto?> Handle(
        GetSavedSearchQuery query, CancellationToken cancellationToken)
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

        var s = await db.SavedSearches
            .AsNoTracking()
            .Where(x => x.Id == savedSearchId && x.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (s is null)
        {
            // Failed-access-detection (ADR 0031): skilj okänt id från cross-tenant.
            var exists = await db.SavedSearches
                .AsNoTracking()
                .AnyAsync(x => x.Id == savedSearchId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "SavedSearch", savedSearchId.Value, currentUser.UserId.Value,
                    "GetSavedSearch");
            }
            return null;
        }

        // ADR 0043 (CTO 2026-05-17, Approach A) — namn-berikningen är scopad
        // till /sokningar-LISTAN (ListSavedSearchesQueryHandler). Detalj-
        // vägen (/sokningar/[id]) renderar inga concept-id (visar namn via
        // körresultat), så labels lämnas tomma här — additiva fält, ingen
        // consumer. Att injicera ITaxonomyReadModel även här vore scope-creep
        // utanför CTO-beslutet + skulle bryta arch-testets
        // "exakt 3 ITaxonomyReadModel-konsumenter"-invariant.
        return new SavedSearchDto(
            s.Id.Value,
            s.Name,
            OccupationGroup: s.Criteria.OccupationGroup,
            Municipality: s.Criteria.Municipality,
            Region: s.Criteria.Region,
            // ADR 0067 Beslut 6 (Fas B2) — råa Klass 2-listor (inga labels, Fas E).
            EmploymentType: s.Criteria.EmploymentType,
            WorktimeExtent: s.Criteria.WorktimeExtent,
            s.Criteria.Q,
            s.Criteria.SortBy,
            s.NotificationEnabled,
            s.LastRunAt,
            s.CreatedAt,
            s.UpdatedAt,
            OccupationGroupLabels: [],
            MunicipalityLabels: [],
            RegionLabels: []);
    }
}
