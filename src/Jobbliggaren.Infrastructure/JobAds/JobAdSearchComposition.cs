using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// ADR 0039 Beslut 1 (SPOT) — den delade sök-kompositionen som flera
/// Infrastructure-queries återanvänder utan att divergera: filter-predikatet
/// (<see cref="ApplyFilter"/>) och DTO-projektionen (<see cref="ToDto"/>).
/// <para>
/// <b>F4-14 (ADR 0076 Decision 5):</b> <see cref="PerUserJobAdSearchQuery"/>
/// (per-användar-match-sort) återanvänder EXAKT samma filter + projektion som
/// <see cref="JobAdSearchQuery"/> (ListJobAds/RunSavedSearch/facets). Filtret är
/// en kompilator-garanterad SPOT — match-sorten kan aldrig träffa en annan
/// annons-mängd än default-sorten. Behaviour-preserving extraktion av den
/// tidigare privata <c>JobAdSearchQuery.ApplyCriteria</c> + inline-projektionen
/// (Fowler 2018 — Extract Function/Move Function); befintliga
/// ListJobAds-/RunSavedSearch-/facet-/FTS-integrationstester är regressions-grind.
/// </para>
/// </summary>
internal static class JobAdSearchComposition
{
    // PostgreSQL text-search-config för svensk stemming. Måste matcha EXAKT
    // den config som search_vector-kolumnen genererades med (JobAdConfiguration
    // — to_tsvector('swedish', …)); annars matchar @@ inte GIN-indexet.
    internal const string TextSearchConfig = "swedish";

    /// <summary>
    /// The single definition of "does this search carry a free-text q?" — the q-FTS predicate below
    /// (<c>search_vector @@ websearch_to_tsquery</c>) and the ts_rank relevance sort are gated on it, and
    /// so is the bitmap-plan count hygiene (<see cref="BitmapPlanCount"/>, #744): the SET LOCAL is worth
    /// its extra roundtrips ONLY when this q-predicate is what would otherwise TOAST-detoast-seqscan the
    /// STORED search_vector. Kept here as SPOT so the hygiene gate can never drift from the predicate it
    /// tracks — a second copy of <c>!IsNullOrWhiteSpace</c> would be a second rule.
    /// </summary>
    internal static bool HasFreeTextQuery([NotNullWhen(true)] string? q) => !string.IsNullOrWhiteSpace(q);

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
    // synas i sök-vägen. SPOT-filter här gör att alla konsumenter
    // (ListJobAds, RunSavedSearch, ListRecentSearches CountAsync, F4-14 match-sort)
    // ärver Status=Active-disciplinen automatiskt (ADR 0039 Beslut 1).
    internal static IQueryable<JobAd> ApplyFilter(
        IQueryable<JobAd> source, JobAdFilterCriteria criteria,
        IOccupationSynonymExpander synonymExpander)
    {
        // ADR 0032-amendment 2026-05-23 — slutanvändar-vyer ser bara Active.
        // ALLOW-LIST (`== Active`), never `!= Archived` (#864 D4): a deny-list silently admits
        // every status added later — the `Erased` Art. 17 tombstone (#842) first, whose empty
        // title and "[raderad]" company name would render in the public /jobb list. Every carrier
        // of this SPOT inherits that choice. Pinned by JobAdSearchLifecycleOracleTests (a real
        // Erased seed — the row where the two forms disagree, reachable since #886).
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
        // #551 PR-B D5 (distans/remote) — remote är en BOOLESK location-sub-axel som
        // UNIONAS in i geo-predikatet (kommun ∨ län ∨ remote), aldrig ett eget AND-Where
        // (annars skulle Distans SKÄRA bort ort-träffar i stället för att bredda). Två
        // grenar, inte en enda guard-predikat-gren: EF Core 10 PARAMETRISERAR captured
        // lokala bool:ar (@__hasMuni) för plan-cache — den vik-konstant-viker dem INTE bort
        // vid translation, så ett unifierat `(hasMuni && muni.Contains) || ... || (remote &&
        // j.Remote)` emitterar `(@__hasMuni AND col IN (...)) OR ...`, INTE byte-identiskt
        // mot dagens `col IN (...)` när remote=false. En Expression.OrElse-kombinator vore
        // byte-identisk men kräver LinqKit (utanför BUILD.md §3.1). Därför: den befintliga
        // muni/län-stegen ORÖRD i else-grenen (Remote=false ⇒ byte-identisk SQL, strukturellt
        // garanterat), och en 4-fallsswitch (parity q-grens-switchen nedan) för remote=true.
        if (criteria.Remote)
        {
            var municipalityValues = criteria.Municipality;
            var regionValues = criteria.Region;
            source = (criteria.Municipality.Count > 0, criteria.Region.Count > 0) switch
            {
                (true, true) => source.Where(j =>
                    j.Remote
                    || municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId"))
                    || regionValues.Contains(EF.Property<string?>(j, "RegionConceptId"))),
                (true, false) => source.Where(j =>
                    j.Remote
                    || municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId"))),
                (false, true) => source.Where(j =>
                    j.Remote
                    || regionValues.Contains(EF.Property<string?>(j, "RegionConceptId"))),
                (false, false) => source.Where(j => j.Remote),
            };
        }
        else if (criteria.Municipality.Count > 0 && criteria.Region.Count > 0)
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

        // #311 D6 (följ arbetsgivare, ADR 0087) — arbetsgivar-facet på org.nr.
        // ORTOGONAL dimension (som Klass 2, INTE geo-union à la kommun/län): eget
        // IN(...)-villkor AND mot allt annat. STORED generated column
        // organization_number (raw_payload->'employer'->>'organization_number'),
        // B-tree partial-indexerad (WHERE … IS NOT NULL), NULL för annons utan org.nr
        // i payload (B2-era: 100% NULL tills re-ingest) → matchas ej ("0 träff" ≠ bug,
        // paritet med övriga taxonomi-dims). org.nr = kanonisk nyckel (ingen fuzzy
        // namn-matchning). Delas av anon- OCH per-användar-vägen (båda kör ApplyFilter).
        if (criteria.Employer.Count > 0)
        {
            var employerValues = criteria.Employer;
            source = source.Where(j =>
                employerValues.Contains(EF.Property<string?>(j, "OrganizationNumber")));
        }

        if (HasFreeTextQuery(criteria.Q))
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
            // spänner title+description). Grinden bor i delade ApplyFilter → den
            // gäller list + count + facets + match-sort samtidigt (ADR 0039 Beslut 1
            // SPOT — ingen list↔count-divergens). Marginell trade-off: <3-teckens
            // mitt-i-ord-substring i titel matchas inte längre; UI-kontraktet
            // (`systemut` → `systemutvecklare`, ADR 0062 Beslut 1) är ≥3 tecken och
            // opåverkat.
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

    // ADR 0039 Beslut 1 (SPOT) — den delade, ANONYMA sorterings-applikationen för de
    // rena JobAdSortBy-axlarna (PublishedAt/ExpiresAt asc/desc + ts_rank-relevans).
    // Behaviour-preserving flytt av den tidigare privata JobAdSearchQuery.ApplySort
    // (Fowler 2018 — Move Function); befintliga ListJobAds/RunSavedSearch/FTS-
    // integrationstester är regressions-grind.
    //
    // ADR 0079 STEG 5 (grad-filter, 2026-06-23): den per-användar-vägen
    // (PerUserJobAdSearchQuery) återanvänder denna sort när användaren grad-filtrerar
    // men sorterar på en ren axel (senast inlagda / kortast ansökningstid / relevans)
    // i stället för match-rank. Det är ISOLERINGS-SÄKERT: sorten läser ENBART
    // j.PublishedAt / j.ExpiresAt / SearchVector+q — ingen per-användar-data — så
    // delningen försvagar inte anon-cachebarheten (grad-WHERE + match-rank-ORDER BY
    // bor kvar 100% i per-användar-queryn). CS8524-disciplin: alla enum-värden
    // explicit + throw på unnamed (validators skyddar; throw = fail-fast vid framtida
    // JobAdSortBy-tillägg).
    internal static IQueryable<JobAd> ApplySort(
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
        if (!HasFreeTextQuery(q))
            return source.OrderByDescending(j => j.PublishedAt).ThenBy(j => j.Id);

        var query = q;
        return source
            .OrderByDescending(j =>
                EF.Property<NpgsqlTsVector>(j, "SearchVector")
                    .Rank(EF.Functions.WebSearchToTsQuery(TextSearchConfig, query)))
            .ThenByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id);
    }

    // ADR 0062/0042 — den delade items-projektionen (Domain → JobAdDto). Återanvänds
    // av både default-sorten (JobAdSearchQuery) och F4-14 match-sorten — ingen
    // DTO-kolumn för sort-nyckeln (Goodhart, ADR 0076 Decision 4). #293/#306
    // (ADR 0042 Beslut E-amendment 2026-06-28): den tidigare tidsbaserade
    // IsNew-projektionen (`PublishedAt >= since`) är BORTTAGEN — "Ny" = OLÄST
    // beräknas nu på FE ur CreatedAt mot den per-användar oläst-watermarken.
    //
    // #745 (epik #737, `d1-list-dto-ships-full-description`) — `j.Description` projiceras
    // INTE längre. En icke-refererad kolumn selekteras inte i den EF-genererade SQL:en, så
    // list-vägen slutar de-TOAST:a den breda `description`-kolumnen per rad för en payload
    // ingen list-yta renderar. `Description` bor kvar på detalj-tråden (GetJobAd → JobAdDetailDto).
    internal static Expression<Func<JobAd, JobAdDto>> ToDto() =>
        j => new JobAdDto(
            j.Id.Value,
            j.Title,
            j.Company.Name,
            j.Url,
            j.Source.Value,
            j.Status.Value,
            j.PublishedAt,
            j.ExpiresAt,
            j.CreatedAt);
}
