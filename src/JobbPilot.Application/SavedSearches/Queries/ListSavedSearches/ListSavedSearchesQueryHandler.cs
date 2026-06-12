using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.SavedSearches.Queries;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.SavedSearches.Queries.ListSavedSearches;

/// <summary>
/// ADR 0043 (CTO 2026-05-17, Approach A) — server-side namn-berikning.
/// <see cref="ITaxonomyReadModel"/> är en Application-port (samma lager) som
/// resolverar concept-id → namn IN-PROCESS via singleton in-memory-cache
/// (O(1) dictionary, ingen DB-/HTTP-touch). Detta är INTE /taxonomy/labels-
/// endpointen → ADR 0043 Beslut D:s reverse-lookup-cap (en HTTP-yta) är
/// irrelevant här; ingen fan-out, ingen ny DoS-yta. Per sparad sökning
/// resolveras Ssyk resp. Region var för sig (matchar test-kontraktet);
/// taket är invariant-bundet (SearchCriteria.MaxConceptIds), ej aggregering.
/// VO/jsonb/ADR 0039-kontrakt orört (additiv read-projektion).
/// </summary>
public sealed class ListSavedSearchesQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, ITaxonomyReadModel taxonomy)
    : IQueryHandler<ListSavedSearchesQuery, IReadOnlyList<SavedSearchDto>>
{
    public async ValueTask<IReadOnlyList<SavedSearchDto>> Handle(
        ListSavedSearchesQuery query, CancellationToken cancellationToken)
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

        var items = await db.SavedSearches
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        // Per sparad sökning: resolvera varje dimension för sig (in-process
        // O(1) singleton-cache, ingen DB-/HTTP-touch, ingen fan-out-DoS —
        // annan yta än /taxonomy/labels-endpointen). Tom lista →
        // ResolveLabelsAsync ger tom lista (ingen krasch). Okänt id →
        // "Okänd kod (<id>)" via portens befintliga fallback-semantik.
        // ADR 0067 Fas C2: OccupationGroup + Municipality ersätter Ssyk.
        var dtos = new List<SavedSearchDto>(items.Count);
        foreach (var s in items)
        {
            var occupationGroupLabels = await taxonomy.ResolveLabelsAsync(
                s.Criteria.OccupationGroup, cancellationToken);
            var municipalityLabels = await taxonomy.ResolveLabelsAsync(
                s.Criteria.Municipality, cancellationToken);
            var regionLabels = await taxonomy.ResolveLabelsAsync(
                s.Criteria.Region, cancellationToken);

            dtos.Add(new SavedSearchDto(
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
                OccupationGroupLabels: occupationGroupLabels,
                MunicipalityLabels: municipalityLabels,
                RegionLabels: regionLabels));
        }

        return dtos;
    }
}
