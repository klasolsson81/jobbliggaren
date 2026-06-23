using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// ADR 0062 — <see cref="IJobAdSearchQuery"/>-implementation. Hela
/// sök-kompositionen (ssyk/region-filter, q-FTS-hybrid, ts_rank-relevans,
/// sortering, paginering, projektion) bor här eftersom PostgreSQL
/// full-text-search-LINQ (<c>websearch_to_tsquery</c> / <c>@@</c> /
/// <c>ts_rank</c>) ligger i Npgsql-assemblyn som arch-testet förbjuder i
/// Application.
/// <para>
/// ADR 0039 Beslut 1 (SPOT) — <see cref="ApplyCriteria"/> är den ENDA
/// filter-vägen; <c>ListJobAds</c> + <c>RunSavedSearch</c> (via
/// <see cref="SearchAsync"/>) och <c>ListRecentSearches</c> (via
/// <see cref="CountAsync"/>) delar den och kan aldrig divergera.
/// Behaviour-preserving flytt av den tidigare
/// <c>Jobbliggaren.Application.JobAds.Queries.JobAdSearch</c>-modulen (Fowler
/// 2018 — Move Function); befintliga ListJobAds-/RunSavedSearch-tester +
/// FTS-integrationstester är regressions-grind.
/// </para>
/// </summary>
internal sealed class JobAdSearchQuery(
    AppDbContext db,
    IOccupationSynonymExpander synonymExpander) : IJobAdSearchQuery
{
    public async ValueTask<PagedResult<JobAdDto>> SearchAsync(
        JobAdSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), criteria.Filter, synonymExpander);

        // Separat count-query (CLAUDE.md §3.6). Filter appliceras före count så
        // totalen reflekterar filtrerad mängd, inte hela korpusen. TD-94 —
        // samma bitmap-plan-tvång som CountAsync/FacetCountsAsync: denna count
        // är ListJobAdsQuery:s fritext-totalCount (TD-94:s headline-konsument)
        // och lider av identisk TOAST-detoast-seqscan. Den efterföljande items-
        // queryn (ts_rank-ordering + paginering) körs UTANFÖR transaktionen och
        // är medvetet orörd (enable_seqscan=off vore fel för ts_rank-vägen).
        var totalCount = await CountWithBitmapPlanAsync(baseQuery.CountAsync, cancellationToken);

        // ADR 0079 STEG 5 — sorten bor nu i den delade JobAdSearchComposition (SPOT),
        // så den per-användar-vägen kan återbruka samma rena sort under grad-filter.
        var ordered = JobAdSearchComposition.ApplySort(baseQuery, criteria.SortBy, criteria.Filter.Q);

        var items = await ordered
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(JobAdSearchComposition.ToDto(criteria.Since))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, criteria.Page, criteria.PageSize);
    }

    public async ValueTask<int> CountAsync(
        JobAdFilterCriteria criteria, CancellationToken cancellationToken)
    {
        // Ren count — ingen sortering, paginering eller projektion. Samma
        // ApplyCriteria-väg som SearchAsync (SPOT). ADR 0060 Beslut 4 N+1
        // capped vid 20.
        var query = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), criteria, synonymExpander);
        return await CountWithBitmapPlanAsync(query.CountAsync, cancellationToken);
    }

    // ADR 0067 Beslut 4 (Fas D1) — per-option facet-counts.
    public async ValueTask<IReadOnlyDictionary<string, int>> FacetCountsAsync(
        JobAdFilterCriteria criteria, FacetDimension dimension,
        CancellationToken cancellationToken)
    {
        // Facett-exkludering: töm den facetterade dimensionens egen lista (tom =
        // inget filter, befintlig JobAdFilterCriteria-semantik) så counten
        // reflekterar alla ANDRA aktiva filter men inte X självt — annars fel
        // siffror vs Platsbanken. SPOT bevarad: ApplyCriteria är fortsatt enda
        // filter-vägen (ingen ApplyCriteriaExcept-duplikat, ADR 0039 Beslut 1 /
        // ADR 0067 Beslut 4).
        var faceted = ExcludeDimension(criteria, dimension);
        var column = ShadowColumn(dimension);

        var baseQuery = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), faceted, synonymExpander);

        // GROUP BY shadow-column → concept-id-count. GROUP BY-translation ligger i
        // Npgsql-assemblyn ⊂ Infrastructure (ADR 0062 Beslut 4 provider-assembly-
        // axel). NULL-shadow exkluderas (annons utan värde på dimensionen) →
        // ingen null-nyckel; predikatet matchar partial-indexet WHERE col IS NOT NULL.
        var groupedQuery = baseQuery
            .Where(j => EF.Property<string?>(j, column) != null)
            .GroupBy(j => EF.Property<string?>(j, column))
            .Select(g => new { ConceptId = g.Key!, Count = g.Count() });

        // TD-94 (CTO-utvidgning 2026-06-13) — facet-counten kör samma
        // ApplyCriteria-q-väg och lider av samma TOAST-detoast-seqscan vid
        // fritext. Samma bitmap-plan-tvång som CountAsync.
        var grouped = await CountWithBitmapPlanAsync(
            ct => groupedQuery.ToListAsync(ct), cancellationToken);

        return grouped.ToDictionary(x => x.ConceptId, x => x.Count, StringComparer.Ordinal);
    }

    // TD-94 (perf-ratchet, ADR 0045 Klass (a) 300 ms p95 warm) — coax the
    // planner to the GIN bitmap for the q-COUNT. A bare COUNT over the
    // FTS-hybrid q-predikatet otherwise Seq Scans and de-TOASTs the wide STORED
    // search_vector column per row (~300–2451 ms warm / ~9 s OS-cold; isolerat
    // bevisat: detoast-delta 487 ms, dotnet-architect-rond 2026-06-13). The GIN
    // Bitmap(Or) plan avoids the detoast (<150 ms warm) men planeraren mis-kostar
    // den eftersom TOAST-detoast-kostnaden inte finns i dess kostnadsmodell.
    //
    // SET LOCAL enable_seqscan = off är transaktions-scopad: den MÅSTE köras på
    // SAMMA pinnade connection som counten (annars no-op utanför transaktionsblock)
    // och återställs vid commit → läcker aldrig till den poolade connectionen
    // (Npgsql pooling-hygien). Rör inte filter-predikatet → ADR 0039 Beslut 1
    // SPOT på filter-semantik intakt; detta är en exekverings-budget-concern, ett
    // annat ansvar (SoC, senior-cto-advisor-dom 2026-06-13, agentId a0472fa5783cdf9ea).
    private async ValueTask<TResult> CountWithBitmapPlanAsync<TResult>(
        Func<CancellationToken, Task<TResult>> count, CancellationToken cancellationToken)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL enable_seqscan = off", cancellationToken);
        var result = await count(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    // FacetDimension → STORED shadow-column (kolumnnamn är Infrastructure-hemlighet;
    // läcker aldrig till Application). Äger GroupBy-nyckelns kolumn, INTE filter-
    // predikatet (det äger ApplyCriteria) — olika ansvar, samma kolumn-konstant.
    private static string ShadowColumn(FacetDimension dimension) => dimension switch
    {
        FacetDimension.OccupationGroup => "OccupationGroupConceptId",
        FacetDimension.Municipality => "MunicipalityConceptId",
        FacetDimension.Region => "RegionConceptId",
        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 STORED-kolumner.
        FacetDimension.EmploymentType => "EmploymentTypeConceptId",
        FacetDimension.WorktimeExtent => "WorktimeExtentConceptId",
        _ => throw new ArgumentOutOfRangeException(
            nameof(dimension), dimension, "Unknown FacetDimension — enum out of sync with ApplyCriteria."),
    };

    // Klonar filter-SPOT:en med den facetterade DIMENSIONENS listor tömda (record
    // with-expression; tom lista = inget filter). Detta är exkluderings-mekaniken
    // (ADR 0067 Beslut 4) — counten för X ska inte filtreras av X.
    //
    // CTO VAL 4 (E2b 2026-06-11, ADR 0067 impl-notat E2b): ort är EN dimension
    // i två granulariteter (län ⊃ kommun, geo-union i ApplyCriteria) —
    // ort-facetterna (Municipality/Region) exkluderar därför HELA
    // ort-dimensionen (båda listorna) ur WHERE. Att exkludera bara den egna
    // listan vore att behandla region som främmande dimension i facetten men
    // samma dimension i WHERE (Evans kap. 2 — samma begrepp, två sanningar).
    private static JobAdFilterCriteria ExcludeDimension(
        JobAdFilterCriteria criteria, FacetDimension dimension) => dimension switch
        {
            FacetDimension.OccupationGroup => criteria with { OccupationGroup = [] },
            FacetDimension.Municipality or FacetDimension.Region =>
                criteria with { Municipality = [], Region = [] },
            // ADR 0067 Beslut 6 (Fas B2) — Klass 2 är ORTOGONALA dimensioner:
            // varje facett exkluderar ENDAST sin egen lista ur WHERE (till
            // skillnad mot ort, där län ⊃ kommun = en dimension i två
            // granulariteter). När man facetterar anställningsform gäller alltså
            // ett aktivt omfattnings-filter fortfarande, och tvärtom.
            FacetDimension.EmploymentType => criteria with { EmploymentType = [] },
            FacetDimension.WorktimeExtent => criteria with { WorktimeExtent = [] },
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension), dimension, "Unknown FacetDimension — enum out of sync with ApplyCriteria."),
        };

}
