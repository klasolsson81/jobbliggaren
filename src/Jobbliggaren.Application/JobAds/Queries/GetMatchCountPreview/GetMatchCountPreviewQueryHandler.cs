using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;

/// <summary>
/// Epik #526 (ADR 0089) — tunn adapter (parity <c>GetFacetCountsQueryHandler</c>): mappar
/// utkastets fyra sök-listor till en <see cref="JobAdFilterCriteria"/> och delegerar till den
/// delade filter-SPOT:en <see cref="IJobAdSearchQuery.CountAsync"/>.
/// <para>
/// <b>Använder den anonymt cachebara <see cref="IJobAdSearchQuery"/>-porten, INTE
/// <c>IPerUserJobAdSearchQuery</c>:</b> sök-preview-counten är ren (ingen grad, ingen profil,
/// ingen per-användar-data), så den hör hemma på match-ren-porten som <c>ListRecentSearches</c>
/// redan räknar via — inte på per-användar-grad-porten (som hade krävt en meningslös inert
/// profil). Samma <c>ApplyFilter</c>-SPOT som <c>/jobb</c>-listan → counten är per konstruktion
/// lika med den länkade sidans <c>TotalCount</c> för samma facetter (ingen siffra↔landning-
/// divergens). Grad-logik hålls därmed helt utanför den cachebara filter-SPOT:en (ADR 0079).
/// </para>
/// <para>
/// <c>WorktimeExtent</c>/<c>Employer</c>/<c>Q</c> är tomma/null — setup-utkastet exponerar
/// varken omfattnings-facett, arbetsgivar-facett eller fritext (ingen fritext-ingress → ingen
/// personnummer-/PII-yta; validatorn låser dessutom varje element till concept-id-format).
/// </para>
/// </summary>
public sealed class GetMatchCountPreviewQueryHandler(IJobAdSearchQuery search)
    : IQueryHandler<GetMatchCountPreviewQuery, MatchCountPreviewDto>
{
    public async ValueTask<MatchCountPreviewDto> Handle(
        GetMatchCountPreviewQuery query, CancellationToken cancellationToken)
    {
        var filter = new JobAdFilterCriteria(
            OccupationGroup: query.OccupationGroups,
            Municipality: query.Municipalities,
            Region: query.Regions,
            EmploymentType: query.EmploymentTypes,
            WorktimeExtent: [],
            Employer: [],
            // #551 PR-B: remote-utkastet wiras i Commit 3 (query.Remote).
            Remote: false,
            Q: null);

        var count = await search.CountAsync(filter, cancellationToken);
        return new MatchCountPreviewDto(count);
    }
}
