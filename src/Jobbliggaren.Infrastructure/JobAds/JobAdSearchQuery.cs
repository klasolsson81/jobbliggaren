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
        // och lider av identisk TOAST-detoast-seqscan. #744 — hygienen gatas på q:
        // utan fritext finns ingen detoast att undvika. Den efterföljande items-
        // queryn (ts_rank-ordering + paginering) körs UTANFÖR transaktionen och
        // är medvetet orörd (enable_seqscan=off vore fel för ts_rank-vägen).
        var totalCount = await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, JobAdSearchComposition.HasFreeTextQuery(criteria.Filter.Q),
            baseQuery.CountAsync, cancellationToken);

        // ADR 0079 STEG 5 — sorten bor nu i den delade JobAdSearchComposition (SPOT),
        // så den per-användar-vägen kan återbruka samma rena sort under grad-filter.
        var ordered = JobAdSearchComposition.ApplySort(baseQuery, criteria.SortBy, criteria.Filter.Q);

        var items = await ordered
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(JobAdSearchComposition.ToDto())
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
        return await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, JobAdSearchComposition.HasFreeTextQuery(criteria.Q),
            query.CountAsync, cancellationToken);
    }

    // #312 (ADR 0115) — antal AKTIVA annonser som matchar `criteria` OCH ingesterats
    // efter `since` (en sparad söknings ResultsSeenAt-watermark). Återanvänder
    // ApplyCriteria-SPOT:en (ADR 0039 Beslut 1 — Status=Active-allow-list +
    // synonym-expansion + filter-paritet; en rå db.JobAds-fönsterfråga skulle tappa
    // både synonymerna → falska negativ OCH #864-Active-livscykelgrinden) och lägger
    // CreatedAt > since-fönstret ovanpå. Fönstret smalnar mängden hårt, men ett
    // q-fritext-kriterium de-TOAST:ar fortfarande search_vector → samma bitmap-plan-
    // tvång som CountAsync (TD-94). Watermark-driven, ej #293/#306:s fasta fönster.
    public async ValueTask<int> CountNewSinceAsync(
        JobAdFilterCriteria criteria, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var query = JobAdSearchComposition
            .ApplyFilter(db.JobAds.AsNoTracking(), criteria, synonymExpander)
            .Where(j => j.CreatedAt > since);
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
        // fritext. Samma bitmap-plan-tvång som CountAsync. #744 — gatad på q
        // (faceted bevarar Q; ExcludeDimension tömmer bara dimensions-listor).
        var grouped = await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, JobAdSearchComposition.HasFreeTextQuery(faceted.Q),
            ct => groupedQuery.ToListAsync(ct), cancellationToken);

        return grouped.ToDictionary(x => x.ConceptId, x => x.Count, StringComparer.Ordinal);
    }

    // FacetDimension → facett-kolumn (kolumnnamn är Infrastructure-hemlighet;
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
            // #551 PR-B D7 — remote är location-dimensionens BOOLESKA sub-axel (unionas
            // med kommun/län i ApplyFilter). Ort-facetten exkluderar därför HELA
            // location-dimensionen INKLUSIVE remote: annars, med Distans aktivt (remote=true),
            // ger Kommun-/Län-popovern nära-noll (WHERE remote ⇒ GROUP BY kommun exkluderar
            // NULL-ort ⇒ tomt), vilket ljuger vs Platsbanken. Icke-location-facetterna nedan
            // BEHÅLLER remote (ett aktivt Distans-filter ska begränsa deras count) — gratis
            // via with-klonen. Beslut 4:s ort-regel, utsträckt till remote.
            FacetDimension.Municipality or FacetDimension.Region =>
                criteria with { Municipality = [], Region = [], Remote = false },
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
