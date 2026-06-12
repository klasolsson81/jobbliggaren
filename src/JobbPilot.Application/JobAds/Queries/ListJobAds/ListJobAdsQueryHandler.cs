using JobbPilot.Application.Common;
using JobbPilot.Application.JobAds.Abstractions;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

/// <summary>
/// Tunn adapter (ADR 0062): mappar <see cref="ListJobAdsQuery"/> till
/// <see cref="JobAdSearchCriteria"/> och delegerar till <see cref="IJobAdSearchQuery"/>.
/// Hela sök-kompositionen (filter, FTS, sort, paginering) bor i Infrastructure-
/// impl:en bakom porten — ADR 0039 Beslut 1 SPOT delas med
/// <c>RunSavedSearchQueryHandler</c>.
/// <para>
/// <b>ADR 0067 Fas D2 (Beslut 5c):</b> live-fritexten (<c>query.Q</c>) är
/// residual-input — den körs genom <see cref="ISearchQueryParser"/> innan den
/// når filter-SPOT:en. <c>ResidualQ</c> matar <c>JobAdFilterCriteria.Q</c> →
/// FTS-hybridens OR-additiva gren (kraschsäker: residual blir aldrig hårt
/// AND-villkor; dimensionerna är separata AND-listor). RunSavedSearch parsar
/// INTE om sitt Q — det är ett persisterat, redan-normaliserat
/// <c>SearchCriteria</c>-värde (validerat vid spar-tid), inte rå residual.
/// </para>
/// </summary>
public sealed class ListJobAdsQueryHandler(IJobAdSearchQuery search, ISearchQueryParser parser)
    : IQueryHandler<ListJobAdsQuery, PagedResult<JobAdDto>>
{
    public ValueTask<PagedResult<JobAdDto>> Handle(
        ListJobAdsQuery query, CancellationToken cancellationToken)
        => search.SearchAsync(
            new JobAdSearchCriteria(
                // null → tom lista: "inget filter" (ADR 0042 Beslut B).
                // ADR 0067 Fas C2 (CTO-dom (e)): Ssyk-dimensionen borttagen ur
                // SPOT:en — q-vägens synonym-expansion mot SsykConceptId drivs
                // separat av Q (recall-substratet orört).
                new JobAdFilterCriteria(
                    OccupationGroup: query.OccupationGroup ?? [],
                    Municipality: query.Municipality ?? [],
                    Region: query.Region ?? [],
                    // ADR 0067 Beslut 6 (Fas B2) — Klass 2 ortogonala dims.
                    EmploymentType: query.EmploymentType ?? [],
                    WorktimeExtent: query.WorktimeExtent ?? [],
                    // ADR 0067 Fas D2 — residual-normalisering före FTS-hybriden.
                    Q: parser.Parse(query.Q).ResidualQ),
                query.SortBy,
                query.Page,
                query.PageSize,
                query.Since),
            cancellationToken);
}
