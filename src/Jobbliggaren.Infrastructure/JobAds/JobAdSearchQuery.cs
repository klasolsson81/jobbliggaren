using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

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
    // PostgreSQL text-search-config för svensk stemming. Måste matcha EXAKT
    // den config som search_vector-kolumnen genererades med (JobAdConfiguration
    // — to_tsvector('swedish', …)); annars matchar @@ inte GIN-indexet.
    private const string TextSearchConfig = "swedish";

    public async ValueTask<PagedResult<JobAdDto>> SearchAsync(
        JobAdSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var baseQuery = ApplyCriteria(db.JobAds.AsNoTracking(), criteria.Filter, synonymExpander);

        // Separat count-query (CLAUDE.md §3.6). Filter appliceras före count så
        // totalen reflekterar filtrerad mängd, inte hela korpusen. TD-94 —
        // samma bitmap-plan-tvång som CountAsync/FacetCountsAsync: denna count
        // är ListJobAdsQuery:s fritext-totalCount (TD-94:s headline-konsument)
        // och lider av identisk TOAST-detoast-seqscan. Den efterföljande items-
        // queryn (ts_rank-ordering + paginering) körs UTANFÖR transaktionen och
        // är medvetet orörd (enable_seqscan=off vore fel för ts_rank-vägen).
        var totalCount = await CountWithBitmapPlanAsync(baseQuery.CountAsync, cancellationToken);

        var ordered = ApplySort(baseQuery, criteria.SortBy, criteria.Filter.Q);

        // ADR 0042 Beslut E — IsNew = PublishedAt inom "Ny sedan"-fönstret.
        // Lokalt fångad nullable → EF översätter jämförelsen (false när Since
        // är null, t.ex. RunSavedSearch).
        var since = criteria.Since;

        var items = await ordered
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(j => new JobAdDto(
                j.Id.Value,
                j.Title,
                j.Company.Name,
                j.Description,
                j.Url,
                j.Source.Value,
                j.Status.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt,
                since != null && j.PublishedAt >= since))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, criteria.Page, criteria.PageSize);
    }

    public async ValueTask<int> CountAsync(
        JobAdFilterCriteria criteria, CancellationToken cancellationToken)
    {
        // Ren count — ingen sortering, paginering eller projektion. Samma
        // ApplyCriteria-väg som SearchAsync (SPOT). ADR 0060 Beslut 4 N+1
        // capped vid 20.
        var query = ApplyCriteria(db.JobAds.AsNoTracking(), criteria, synonymExpander);
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

        var baseQuery = ApplyCriteria(db.JobAds.AsNoTracking(), faceted, synonymExpander);

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

    // F2-P9 (TD-70). Filter via Postgres STORED generated columns (B-tree-
    // indexerade, equality-lookup). Shadow-properties refereras via
    // EF.Property<string?>(…) — de är inte top-level Domain-fält (Evans 2003
    // §14 ACL — JobTech-taxonomi är inte Jobbliggarens ubiquitous language).
    // ADR 0042 Beslut B — multi → SQL IN(…) via list.Contains.
    //
    // ADR 0067 Beslut 1 (Platsbanken sök-paritet Fas C1, Variant C) — yrke-
    // nivåbyte: det explicita yrke-filtret targetar OccupationGroupConceptId
    // (ssyk-level-4/yrkesgrupp) i stället för SsykConceptId (occupation-name).
    // Den tidigare Ssyk-equality-grenen är BORTTAGEN. SsykConceptId-kolumnen
    // lever vidare i q-vägens synonym-expansion nedan (recall-substrat bevarat).
    // Municipality (kommun) tillkommer som ny dimension (analogt Region).
    //
    // ADR 0062 — q-FTS-hybrid. FTS-grenen (search_vector @@ websearch_to_tsquery)
    // är den snabba primärvägen: GIN-index på tsvector + svensk stemming
    // (lärare/läraren → samma lexem). title-LIKE-grenen är en billig
    // substring-fallback för mitt-i-ord-matchning ("systemut" →
    // "systemutvecklare") — titlar är korta, ingen TOAST, träffar
    // ix_job_ads_title_lower_trgm. description-LIKE körs ALDRIG i q-grenen:
    // det var perf-rotorsaken (EXPLAIN ANALYZE 2026-05-21 — de-TOAST av ~13k
    // description-texter, trigram-selektivitet 7 581 falska positiva för
    // "lärare"; ADR 0061 → ADR 0062).
    //
    // ADR 0032-amendment 2026-05-23 + ADR 0062-amendment 2026-05-23: Archived-
    // JobAds (snapshot-retention + ExpiresAt-cron + stream-removal) får ALDRIG
    // synas i sök-vägen. SPOT-filter här gör att alla tre konsumenter
    // (ListJobAds, RunSavedSearch, ListRecentSearches CountAsync) ärver
    // Status=Active-disciplinen automatiskt (ADR 0039 Beslut 1).
    private static IQueryable<JobAd> ApplyCriteria(
        IQueryable<JobAd> source, JobAdFilterCriteria criteria,
        IOccupationSynonymExpander synonymExpander)
    {
        // ADR 0032-amendment 2026-05-23 — slutanvändar-vyer ser bara Active.
        source = source.Where(j => j.Status == JobAdStatus.Active);

        if (criteria.OccupationGroup.Count > 0)
        {
            var groupValues = criteria.OccupationGroup;
            source = source.Where(j => groupValues.Contains(EF.Property<string?>(j, "OccupationGroupConceptId")));
        }

        // ADR 0067 implementerings-notat E2b (CTO VAL 1, 2026-06-11) — Ort är
        // EN dimension i två granulariteter (län ⊃ kommun, inte ortogonala
        // axlar). När BÅDA listorna är icke-tomma: inkluderande union
        // (kommun-träff ELLER region-träff) — speglar JobTech/Platsbankens
        // web-verifierade geografi-semantik ("most local promoted" = union,
        // GettingStartedJobSearchEN.md). Sekventiella AND-Where gav noll
        // träffar för region=län-X + kommun-i-län-Y. Ensam lista: oförändrad
        // gren (OR-inom-dimension via IN(...) som förut). AND mot övriga
        // dimensioner (yrke/q) består — ADR 0067 Beslut 5-invarianten gäller
        // ortogonala dimensioner.
        if (criteria.Municipality.Count > 0 && criteria.Region.Count > 0)
        {
            var municipalityValues = criteria.Municipality;
            var regionValues = criteria.Region;
            source = source.Where(j =>
                municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId"))
                || regionValues.Contains(EF.Property<string?>(j, "RegionConceptId")));
        }
        else if (criteria.Municipality.Count > 0)
        {
            var municipalityValues = criteria.Municipality;
            source = source.Where(j => municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId")));
        }
        else if (criteria.Region.Count > 0)
        {
            var regionValues = criteria.Region;
            source = source.Where(j => regionValues.Contains(EF.Property<string?>(j, "RegionConceptId")));
        }

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
        // ORTOGONALA dimensioner (oberoende axlar, ej geo-union à la kommun/län):
        // var lista är ett eget IN(...)-villkor AND mot allt annat. STORED
        // generated columns (employment_type_concept_id / worktime_extent_concept_id),
        // B-tree-indexerade, NULL för annons utan key i payload (purgad/saknad)
        // → matchas ej (paritet med övriga taxonomi-dims; "0 träff" ≠ bug).
        if (criteria.EmploymentType.Count > 0)
        {
            var employmentTypeValues = criteria.EmploymentType;
            source = source.Where(j =>
                employmentTypeValues.Contains(EF.Property<string?>(j, "EmploymentTypeConceptId")));
        }

        if (criteria.WorktimeExtent.Count > 0)
        {
            var worktimeExtentValues = criteria.WorktimeExtent;
            source = source.Where(j =>
                worktimeExtentValues.Contains(EF.Property<string?>(j, "WorktimeExtentConceptId")));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Q))
        {
            var q = criteria.Q;
            // title-LIKE-fallbacken lower:as redan invariant-side (C#); EF/Npgsql
            // translaterar .ToLower() (utan culture-arg) till SQL LOWER(col).
            // CA1304/CA1311-suppress: LINQ-translation till SQL, inte runtime-
            // string-op — culture är irrelevant. websearch_to_tsquery sköter
            // sin egen normalisering (lexem-tokenisering, robust mot user-input,
            // kastar aldrig på dålig syntax).
            //
            // STEG 6 Approach B (2026-05-24) — SSYK-expansion ovanpå FTS+title-LIKE.
            // synonymExpander översätter fritext ("systemutvecklare") till JobTech
            // occupation-concept_ids via konfigurerad mapping. OR-additiv: ökar
            // recall utan att sänka precision för existing FTS-träffar. Q-fältet
            // består — vi ENBART utvidgar matchnings-ytan med SSYK-träffar för
            // annonser som har ssyk_concept_id satt. Backfill från Approach A ger
            // ~88% av korpus med populerad ssyk_concept_id (CTO-rond Plan C-design,
            // architect-rond 2026-05-24).
            var pattern = $"%{q.ToLowerInvariant()}%";
            var expandedSsyks = synonymExpander.Expand(q);

            // TD-94 (perf-ratchet, ADR 0045) / ADR 0062-amendment 2026-06-13 —
            // title-LIKE-grenen körs ENDAST för q ≥ 3 tecken. GIN-trigram kan
            // fysiskt inte serva en <3-teckens LIKE '%q%' (trigram = 3-grams) →
            // för korta q tvingas en btree-prefix-/seq-scan över hela korpusen
            // (42 873 rader, ~346 ms) trots att FTS-grenen ensam är selektiv.
            // FTS-lexem-matchningen täcker korta vanliga termer ändå (search_vector
            // spänner title+description). Grinden bor i delade ApplyCriteria → den
            // gäller list + count + facets samtidigt (ADR 0039 Beslut 1 SPOT —
            // ingen list↔count-divergens). Marginell trade-off: <3-teckens mitt-i-
            // ord-substring i titel matchas inte längre; UI-kontraktet (`systemut`
            // → `systemutvecklare`, ADR 0062 Beslut 1) är ≥3 tecken och opåverkat.
            var includeTitleLike = q.Length >= 3;

#pragma warning disable CA1304, CA1311
            source = (includeTitleLike, hasSsyks: expandedSsyks.Count > 0) switch
            {
                (true, true) => source.Where(j =>
                    EF.Property<NpgsqlTsVector>(j, "SearchVector")
                        .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, q))
                    || EF.Functions.Like(j.Title.ToLower(), pattern)
                    || expandedSsyks.Contains(EF.Property<string?>(j, "SsykConceptId"))),
                (true, false) => source.Where(j =>
                    EF.Property<NpgsqlTsVector>(j, "SearchVector")
                        .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, q))
                    || EF.Functions.Like(j.Title.ToLower(), pattern)),
                (false, true) => source.Where(j =>
                    EF.Property<NpgsqlTsVector>(j, "SearchVector")
                        .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, q))
                    || expandedSsyks.Contains(EF.Property<string?>(j, "SsykConceptId"))),
                (false, false) => source.Where(j =>
                    EF.Property<NpgsqlTsVector>(j, "SearchVector")
                        .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, q))),
            };
#pragma warning restore CA1304, CA1311
        }

        return source;
    }

    // Alla enum-värden explicit listade + throw på unnamed (CS8524-disciplin).
    // Validators (ListJobAdsQueryValidator / SearchCriteria.Create) skyddar mot
    // invalid values innan denna metod — throw är defense-in-depth (fail-fast
    // vid framtida JobAdSortBy-tillägg: case missas → ArgumentOutOfRangeException
    // → integrationstest fail).
    private static IQueryable<JobAd> ApplySort(
        IQueryable<JobAd> source, JobAdSortBy sortBy, string? q) =>
        sortBy switch
        {
            JobAdSortBy.PublishedAtDesc => source.OrderByDescending(j => j.PublishedAt).ThenBy(j => j.Id),
            JobAdSortBy.PublishedAtAsc => source.OrderBy(j => j.PublishedAt).ThenBy(j => j.Id),
            // NULL-ExpiresAt sorteras sist (har inget slut-datum = pågående).
            JobAdSortBy.ExpiresAtDesc =>
                source.OrderBy(j => j.ExpiresAt == null)
                      .ThenByDescending(j => j.ExpiresAt)
                      .ThenBy(j => j.Id),
            JobAdSortBy.ExpiresAtAsc =>
                source.OrderBy(j => j.ExpiresAt == null)
                      .ThenBy(j => j.ExpiresAt)
                      .ThenBy(j => j.Id),
            // ADR 0062 — ts_rank ersätter den tidigare ILIKE-3-2-1-heuristiken
            // (ADR 0042 Beslut D2). Relevans-rankning av FTS-träffar.
            JobAdSortBy.Relevance => ApplyRelevanceSort(source, q),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy), sortBy, "Unknown JobAdSortBy — validator should reject."),
        };

    // ADR 0062 — relevans-sort via PostgreSQL ts_rank(search_vector,
    // websearch_to_tsquery('swedish', q)). Relevance kräver q non-null
    // (invariant i SearchCriteria.Create + ListJobAdsQueryValidator);
    // null-guarden är defense-in-depth (fallback PublishedAt desc, kastar ej i
    // query-vägen). Rader som matchade enbart via title-LIKE-fallbacken (ej
    // FTS) får ts_rank 0 → de sorteras efter FTS-träffarna, sedan PublishedAt
    // desc, sedan Id.
    private static IQueryable<JobAd> ApplyRelevanceSort(IQueryable<JobAd> source, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return source.OrderByDescending(j => j.PublishedAt).ThenBy(j => j.Id);

        var query = q;
        return source
            .OrderByDescending(j =>
                EF.Property<NpgsqlTsVector>(j, "SearchVector")
                    .Rank(EF.Functions.WebSearchToTsQuery(TextSearchConfig, query)))
            .ThenByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id);
    }
}
