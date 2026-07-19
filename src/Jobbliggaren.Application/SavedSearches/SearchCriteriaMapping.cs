using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.SavedSearches;

/// <summary>
/// ADR 0039 Beslut 1 (SPOT) — den ENDA översättningen av en persisterad
/// <see cref="SearchCriteria"/>-VO till den delade <see cref="JobAdFilterCriteria"/>
/// (IJobAdSearchQuery-filter-SPOT:en). Ett ställe så att en ny kriterie-dimension på
/// VO:t (t.ex. #311 Employer, #551 Remote) reproduceras IDENTISKT i varje sparad-
/// söknings-läsväg — <c>RunSavedSearchQueryHandler</c> (körning) och #312
/// <c>GetNewSavedSearchResultsCountQueryHandler</c> (in-app "nya träffar"-räkning) kan
/// aldrig divergera. Utan denna SPOT vore de två handlarna två sanningar om vad ett
/// sparat filter betyder (Evans 2003 kap. 2; ADR 0039 Beslut 1-disciplinen).
/// </summary>
internal static class SearchCriteriaMapping
{
    public static JobAdFilterCriteria ToFilterCriteria(SearchCriteria criteria) =>
        new(
            OccupationGroup: criteria.OccupationGroup,
            Municipality: criteria.Municipality,
            Region: criteria.Region,
            EmploymentType: criteria.EmploymentType,
            WorktimeExtent: criteria.WorktimeExtent,
            Employer: criteria.Employer,
            Remote: criteria.Remote,
            Q: criteria.Q);
}
