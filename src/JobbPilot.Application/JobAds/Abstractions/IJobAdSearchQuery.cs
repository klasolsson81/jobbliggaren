using JobbPilot.Application.Common;
using JobbPilot.Application.JobAds.Queries;

namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Application-port för jobbannons-sök (ADR 0062). Hela sök-kompositionen —
/// ssyk/region-filter, q-FTS-hybrid, ts_rank-relevans, sortering och
/// paginering — bor i Infrastructure-impl:en <c>JobAdSearchQuery</c> eftersom
/// PostgreSQL full-text-search-LINQ (<c>websearch_to_tsquery</c> / <c>@@</c> /
/// <c>ts_rank</c>) fysiskt ligger i <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>
/// — en assembly som arch-testet förbjuder i Application (CLAUDE.md §2.1,
/// ADR 0001 Dependency Rule).
/// <para>
/// Porten bevarar ADR 0039 Beslut 1 SPOT: tre konsumenter delar samma
/// filter-väg och kan aldrig divergera — <c>ListJobAdsQueryHandler</c> +
/// <c>RunSavedSearchQueryHandler</c> via <see cref="SearchAsync"/>, och
/// <c>ListRecentSearchesQueryHandler</c> via <see cref="CountAsync"/>.
/// Speglar <see cref="IJobSource"/> / <see cref="ITaxonomyReadModel"/>
/// (Application-port, internal Infrastructure-impl, ren DTO över gränsen).
/// </para>
/// </summary>
public interface IJobAdSearchQuery
{
    /// <summary>
    /// Filtrerar, rangordnar och paginerar jobbannonser enligt
    /// <paramref name="criteria"/>. Returnerar en ren Application-DTO-sida —
    /// ingen EF-entity passerar Application-gränsen (CLAUDE.md §5.1). Separat
    /// count-query körs före paginering (CLAUDE.md §3.6).
    /// </summary>
    ValueTask<PagedResult<JobAdDto>> SearchAsync(
        JobAdSearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>
    /// Räknar matchande jobbannonser för <paramref name="criteria"/> utan
    /// sortering, paginering eller projektion. Samma filter-predikat som
    /// <see cref="SearchAsync"/> (delad <see cref="JobAdFilterCriteria"/> →
    /// SPOT). Konsumeras av <c>ListRecentSearchesQueryHandler</c> för
    /// per-sökning-träffräkning (ADR 0060 Beslut 4 — N+1 capped vid 20).
    /// </summary>
    ValueTask<int> CountAsync(
        JobAdFilterCriteria criteria, CancellationToken cancellationToken);
}
