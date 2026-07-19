using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Queries;

namespace Jobbliggaren.Application.JobAds.Abstractions;

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

    /// <summary>
    /// #312 (ADR 0115) — räknar AKTIVA jobbannonser som matchar
    /// <paramref name="criteria"/> OCH ingesterades efter <paramref name="since"/>
    /// (en sparad söknings per-sökning-<c>ResultsSeenAt</c>-watermark). Samma
    /// filter-predikat som <see cref="SearchAsync"/>/<see cref="CountAsync"/> (delad
    /// <see cref="JobAdFilterCriteria"/> → SPOT: <c>Status=Active</c>-allow-list +
    /// synonym-expansion inkluderad — en rå db.JobAds-fönsterfråga skulle tappa både
    /// synonymerna → falska negativ och #864-livscykelgrinden) plus ett
    /// <c>CreatedAt &gt; since</c>-fönster. Watermark-driven (INTE #293/#306:s retirerade
    /// fasta "Ny"-fönster — <paramref name="since"/> är användarens läs-mark, ej ett fast
    /// N-dygns-fönster). Konsumeras av <c>GetNewSavedSearchResultsCountQueryHandler</c>
    /// för in-app "nya träffar"-räkningen (in-app-only v1, GDPR Art. 6(1)(b)).
    /// </summary>
    ValueTask<int> CountNewSinceAsync(
        JobAdFilterCriteria criteria, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// Per-option facet-counts för <paramref name="dimension"/> (ADR 0067
    /// Beslut 4 — Platsbanken sök-paritet Fas D1). Returnerar
    /// concept-id → antal matchande aktiva annonser, t.ex. "Mörbylånga (34)".
    /// <para>
    /// <b>Facett-exkluderings-semantik:</b> counten reflekterar alla andra
    /// aktiva filter i <paramref name="criteria"/> men INTE
    /// <paramref name="dimension"/> självt (annars fel siffror vs Platsbanken —
    /// en användare som redan valt en yrkesgrupp ska ändå se hur många annonser
    /// varje *annan* yrkesgrupp skulle ge). Mekanik: <c>ApplyCriteria</c> körs
    /// med den facetterade DIMENSIONENS listor tömda (SPOT bevarad — ingen
    /// andra filter-väg), följt av GROUP BY på dimensionens facett-kolumn.
    /// <b>Ort-facetterna (Municipality/Region) exkluderar HELA ort-dimensionen</b>
    /// (båda listorna) — län ⊃ kommun är EN dimension i två granulariteter
    /// (geo-union, CTO VAL 4 E2b 2026-06-11 / ADR 0067 impl-notat E2b).
    /// </para>
    /// <para>
    /// Rå concept-id (namn-omedveten, ADR 0043 Beslut E — label-resolution är
    /// <see cref="ITaxonomyReadModel"/>/FE-ansvar). NULL-shadow-värden (annons
    /// utan värde på dimensionen) exkluderas → ingen null-nyckel i resultatet.
    /// Ny omätt hot-path mot ~44k rader → NBomber-gate mot ADR 0045 (300 ms p95)
    /// FÖRE live-aktivering.
    /// </para>
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, int>> FacetCountsAsync(
        JobAdFilterCriteria criteria, FacetDimension dimension,
        CancellationToken cancellationToken);
}
