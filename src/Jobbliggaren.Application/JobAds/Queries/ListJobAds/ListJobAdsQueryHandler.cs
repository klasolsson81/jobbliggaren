using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

/// <summary>
/// Adapter (ADR 0062): mappar <see cref="ListJobAdsQuery"/> till
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
/// <para>
/// <b>F4-14 (ADR 0076 Decision 4/5/7):</b> "Sortera efter matchning"
/// (<c>query.SortByMatch</c>) grenar till den per-användar-match-sort-porten
/// <see cref="IPerUserJobAdSearchQuery"/>. Profilen byggs ur lagrade
/// preferenser (ingen CV-läsning). Decision 7 honest fallback: ingen angiven
/// yrkesgrupp (tom SSYK-gate) → faller tillbaka till den rena default-sorten
/// (<c>SortBy</c> == PublishedAtDesc för en match-begäran), aldrig en fejkad
/// ordning. <see cref="IJobAdSearchQuery"/> förblir match-ren — match-datan
/// rör aldrig den delade SPOT-porten (Decision 5).
/// </para>
/// </summary>
public sealed class ListJobAdsQueryHandler(
    IJobAdSearchQuery search,
    IPerUserJobAdSearchQuery perUserSearch,
    IMatchProfileBuilder profileBuilder,
    ISearchQueryParser parser)
    : IQueryHandler<ListJobAdsQuery, PagedResult<JobAdDto>>
{
    public async ValueTask<PagedResult<JobAdDto>> Handle(
        ListJobAdsQuery query, CancellationToken cancellationToken)
    {
        // null → tom lista: "inget filter" (ADR 0042 Beslut B).
        // ADR 0067 Fas C2 (CTO-dom (e)): Ssyk-dimensionen borttagen ur SPOT:en —
        // q-vägens synonym-expansion mot SsykConceptId drivs separat av Q.
        // ADR 0067 Fas D2 — residual-normalisering före FTS-hybriden. Parsas EN
        // gång; samma filter används av båda grenarna (samma träff-mängd, SPOT).
        var filter = new JobAdFilterCriteria(
            OccupationGroup: query.OccupationGroup ?? [],
            Municipality: query.Municipality ?? [],
            Region: query.Region ?? [],
            EmploymentType: query.EmploymentType ?? [],
            WorktimeExtent: query.WorktimeExtent ?? [],
            Q: parser.Parse(query.Q).ResidualQ);

        // ADR 0079 STEG 5 — enter the per-user path when EITHER a grade filter is
        // active (MatchContextActive) OR the user asked for match-rank order
        // (SortByMatch). Decoupled: a grade filter composes with ANY sort (senast
        // inlagda / kortast ansökningstid / relevans / bästa matchning).
        if (query.MatchContextActive || query.SortByMatch)
        {
            // F4-15 (ADR 0076 Decision 6, R5-REBIND Option H): the global sort builds the
            // FULL profile from the confirmed plaintext skill set (ADR 0079 STEG 3 PR-D) —
            // NO DEK on the hottest path. The skill signal adds only the binary golden rung.
            var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);

            // SSYK-gate (parity F4-13 GetJobAdMatchBatch): utan angiven yrkesgrupp kan
            // ingen annons få en grad → grad-filter/match-ordning vore meningslös. Honest
            // fallback till default-sorten (Decision 7) med den valda sorten, aldrig en
            // tom grad-filtrerad sida (CTO-re-bind case 2). FE döljer kontrollen då.
            if (profile.Fast.SsykGroupConceptIds.Count > 0)
            {
                return await perUserSearch.SearchPerUserAsync(
                    filter,
                    profile,
                    grades: query.MatchGrades ?? [],
                    sort: query.SortBy,
                    orderByMatchRank: query.SortByMatch,
                    query.Page,
                    query.PageSize,
                    query.Since,
                    cancellationToken);
            }
        }

        return await search.SearchAsync(
            new JobAdSearchCriteria(
                filter, query.SortBy, query.Page, query.PageSize, query.Since),
            cancellationToken);
    }
}
