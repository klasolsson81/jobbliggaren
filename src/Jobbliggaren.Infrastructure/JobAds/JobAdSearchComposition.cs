using System.Linq.Expressions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
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

    // F2-P9 (TD-70). Filter via de mappade Postgres-kolumnerna (B-tree-
    // indexerade, equality-lookup). De sju taxonomi-/org.nr-kolumnerna är sedan
    // #841/#873 vanliga mappade JobAd-properties (läses som j.X, kompilator-
    // kontrollerat) — de bär JobTech-taxonomikoder, inte Jobbliggarens ubiquitous
    // language (Evans 2003 §14 ACL). Bara search_vector/extracted_lexemes är shadow.
    // ADR 0042 Beslut B — multi → SQL IN(…) via list.Contains.
    //
    // ADR 0067 Beslut 1 (Platsbanken sök-paritet Fas C1, Variant C) — yrke-
    // nivåbyte: det explicita yrke-filtret targetar OccupationGroupConceptId
    // (ssyk-level-4/yrkesgrupp) i stället för OccupationNameConceptId (occupation-name).
    // Den tidigare Ssyk-equality-grenen är BORTTAGEN. OccupationNameConceptId-kolumnen
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
    //
    // #2143 (ADR 0167) — `root` must be the unfiltered job_ads set: each minus-word is a correlated
    // NOT EXISTS over it, kept free of the Active filter so the planner serves the inner side from the
    // GIN indexes rather than from the status index.
    internal static IQueryable<JobAd> ApplyFilter(IQueryable<JobAd> root, ResolvedJobAdFilter filter)
    {
        var criteria = filter.Criteria;
        var text = filter.Text;

        // ADR 0032-amendment 2026-05-23 — slutanvändar-vyer ser bara Active.
        // ALLOW-LIST (`== Active`), never `!= Archived` (#864 D4): a deny-list silently admits
        // every status added later — the `Erased` Art. 17 tombstone (#842) first, whose empty
        // title and "[raderad]" company name would render in the public /jobb list. Every carrier
        // of this SPOT inherits that choice. Pinned by JobAdSearchLifecycleOracleTests (a real
        // Erased seed — the row where the two forms disagree, reachable since #886).
        var source = root.Where(j => j.Status == JobAdStatus.Active);

        if (criteria.OccupationGroup.Count > 0)
        {
            var groupValues = criteria.OccupationGroup;
            source = source.Where(j => groupValues.Contains(j.OccupationGroupConceptId));
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
        //
        // #1413 — termerna som UNIONERAS här är klassificerade i GeoUnionLabelParityTests.GeoUnion.
        // Lägger du till en term här utan att flytta den dit beskriver recent-sökningens etikett en
        // strikt delmängd av vad klicket returnerar; vakten mäter klassificeringen, inte den här
        // grenen, så den kan inte se ändringen.
        if (criteria.Remote)
        {
            var municipalityValues = criteria.Municipality;
            var regionValues = criteria.Region;
            source = (criteria.Municipality.Count > 0, criteria.Region.Count > 0) switch
            {
                (true, true) => source.Where(j =>
                    j.Remote
                    || municipalityValues.Contains(j.MunicipalityConceptId)
                    || regionValues.Contains(j.RegionConceptId)),
                (true, false) => source.Where(j =>
                    j.Remote
                    || municipalityValues.Contains(j.MunicipalityConceptId)),
                (false, true) => source.Where(j =>
                    j.Remote
                    || regionValues.Contains(j.RegionConceptId)),
                (false, false) => source.Where(j => j.Remote),
            };
        }
        else if (criteria.Municipality.Count > 0 && criteria.Region.Count > 0)
        {
            var municipalityValues = criteria.Municipality;
            var regionValues = criteria.Region;
            source = source.Where(j =>
                municipalityValues.Contains(j.MunicipalityConceptId)
                || regionValues.Contains(j.RegionConceptId));
        }
        else if (criteria.Municipality.Count > 0)
        {
            var municipalityValues = criteria.Municipality;
            source = source.Where(j => municipalityValues.Contains(j.MunicipalityConceptId));
        }
        else if (criteria.Region.Count > 0)
        {
            var regionValues = criteria.Region;
            source = source.Where(j => regionValues.Contains(j.RegionConceptId));
        }

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
        // ORTOGONALA dimensioner (oberoende axlar, ej geo-union à la kommun/län):
        // var lista är ett eget IN(...)-villkor AND mot allt annat. Mappade
        // C#-skrivna kolumner (employment_type_concept_id / worktime_extent_concept_id),
        // B-tree-indexerade, NULL för annons utan key i payload (purgad/saknad)
        // → matchas ej (paritet med övriga taxonomi-dims; "0 träff" ≠ bug).
        if (criteria.EmploymentType.Count > 0)
        {
            var employmentTypeValues = criteria.EmploymentType;
            source = source.Where(j =>
                employmentTypeValues.Contains(j.EmploymentTypeConceptId));
        }

        if (criteria.WorktimeExtent.Count > 0)
        {
            var worktimeExtentValues = criteria.WorktimeExtent;
            source = source.Where(j =>
                worktimeExtentValues.Contains(j.WorktimeExtentConceptId));
        }

        // #311 D6 (följ arbetsgivare, ADR 0087) — arbetsgivar-facet på org.nr.
        // ORTOGONAL dimension (som Klass 2, INTE geo-union à la kommun/län): eget
        // IN(...)-villkor AND mot allt annat. Mappad C#-skriven kolumn
        // organization_number (skriven vid ingest ur raw_payload->'employer'->>'organization_number'),
        // B-tree partial-indexerad (WHERE … IS NOT NULL), NULL för annons utan org.nr
        // i payload (B2-era: 100% NULL tills re-ingest) → matchas ej ("0 träff" ≠ bug,
        // paritet med övriga taxonomi-dims). org.nr = kanonisk nyckel (ingen fuzzy
        // namn-matchning). Delas av anon- OCH per-användar-vägen (båda kör ApplyFilter).
        if (criteria.Employer.Count > 0)
        {
            var employerValues = criteria.Employer;
            source = source.Where(j =>
                employerValues.Contains(j.OrganizationNumber));
        }

        if (text.Positive is { } positive)
            source = source.Where(MatchesText(positive));

        // A correlated NOT EXISTS, never an inline NOT: inline, the full-text arm would detoast
        // search_vector for every row the outer scan reaches (TD-94), and NOT over a NULL ssyk arm is
        // NULL, which would drop the row. Inside EXISTS the predicate stays positive and GIN-served.
        foreach (var term in text.Excluded)
        {
            var excludedAds = root.Where(MatchesText(term));
            source = source.Where(j => !excludedAds.Any(x => x.Id == j.Id));
        }

        return ExcludeNamedConcepts(source, text.ExcludedLabels);
    }

    // The q match, one term at a time, shared by the positive residual and every minus-word.
    //
    // title-LIKE-fallbacken lower:as redan invariant-side (C#); EF/Npgsql
    // translaterar .ToLower() (utan culture-arg) till SQL LOWER(col).
    // CA1304/CA1311-suppress: LINQ-translation till SQL, inte runtime-
    // string-op — culture är irrelevant. websearch_to_tsquery sköter
    // sin egen normalisering (lexem-tokenisering, robust mot user-input,
    // kastar aldrig på dålig syntax).
    //
    // STEG 6 Approach B (2026-05-24) — SSYK-expansion ovanpå FTS+title-LIKE.
    // IOccupationSynonymExpander översätter fritext ("systemutvecklare") till JobTech
    // occupation-concept_ids via konfigurerad mapping. OR-additiv: ökar
    // recall utan att sänka precision för existing FTS-träffar. Q-fältet
    // består — vi ENBART utvidgar matchnings-ytan med SSYK-träffar för
    // annonser som har ssyk_concept_id satt. Backfill från Approach A ger
    // ~88% av korpus med populerad ssyk_concept_id (CTO-rond Plan C-design,
    // architect-rond 2026-05-24).
    //
    // TD-94 (perf-ratchet, ADR 0045) / ADR 0062-amendment 2026-06-13 —
    // title-LIKE-grenen körs ENDAST för en term med en följd av ≥ 3 bokstäver/siffror
    // (TextTerm.ContainsPattern är null annars, #2143). GIN-trigram kan fysiskt inte serva
    // en LIKE '%q%' utan ett helt trigram → hela indexet läses och rechecken detoastar
    // varje rad, trots att FTS-grenen ensam är selektiv. FTS-lexem-matchningen täcker
    // korta vanliga termer ändå (search_vector spänner title+description). Marginell
    // trade-off: <3-teckens mitt-i-ord-substring i titel matchas inte längre; UI-
    // kontraktet (`systemut` → `systemutvecklare`, ADR 0062 Beslut 1) är ≥3 tecken och
    // opåverkat.
    //
    // #1546 - the same gate now carries the company-name branch below, for the same
    // reason: ix_job_ads_company_name_lower_trgm is a trigram index and cannot serve a
    // <3-character LIKE either. That branch exists because the hero field promises
    // "Sök efter yrke, arbetsgivare eller ort" while search_vector spans title +
    // description only, so an employer name reached nothing at all.
    // search_vector is deliberately NOT widened: it is a STORED generated column, so
    // redefining it rewrites the whole table under ACCESS EXCLUSIVE on a continuously
    // ingested table, where this branch costs one CONCURRENTLY-built index.
    //
    // #2143 — the full-text arm comes LAST, and the order is load-bearing: PostgreSQL 18's executor
    // evaluates the arms as written and stops at the first true (measured, not guaranteed: docs
    // §4.2.14), so a row whose title, employer or SSYK already matches never has its search_vector
    // detoasted in a recheck or a filter. Measured by ExcludedTermLatencyMeasurement.
    private static Expression<Func<JobAd, bool>> MatchesText(TextTerm term)
    {
        var q = term.Term;
        var expandedSsyks = term.Ssyks;
        var arms = new List<Expression<Func<JobAd, bool>>>(4);

#pragma warning disable CA1304, CA1311
        if (term.ContainsPattern is { } pattern)
        {
            arms.Add(j => EF.Functions.Like(j.Title.ToLower(), pattern, LikePatterns.EscapeCharacter));
            arms.Add(j => EF.Functions.Like(j.Company.Name.ToLower(), pattern, LikePatterns.EscapeCharacter));
        }
#pragma warning restore CA1304, CA1311

        if (expandedSsyks.Count > 0)
            arms.Add(j => expandedSsyks.Contains(j.OccupationNameConceptId));

        if (term.Role == TextTermRole.Excluded)
        {
            arms.Add(j => EF.Property<NpgsqlTsVector>(j, "SearchVector")
                .Matches(EF.Functions.PlainToTsQuery(TextSearchConfig, q)));
        }
        else
        {
            arms.Add(j => EF.Property<NpgsqlTsVector>(j, "SearchVector")
                .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, q)));
        }

        return arms.Aggregate(OrElse);
    }

    private static Expression<Func<JobAd, bool>> OrElse(
        Expression<Func<JobAd, bool>> left, Expression<Func<JobAd, bool>> right) =>
        Expression.Lambda<Func<JobAd, bool>>(
            Expression.OrElse(
                left.Body,
                ReplacingExpressionVisitor.Replace(right.Parameters[0], left.Parameters[0], right.Body)),
            left.Parameters);

    // #2143 (ADR 0167) — the concepts a minus-word names exactly (ITaxonomyLabelMatcher). Inline, unlike
    // the text arm: these are B-tree columns with no detoast cost. Each column is nullable, and an ad
    // with no value names nothing, so it survives — written out as `== null ||` rather than left to
    // EF's null compensation.
    private static IQueryable<JobAd> ExcludeNamedConcepts(
        IQueryable<JobAd> source, TaxonomyLabelMatchDto labels)
    {
        if (labels.Municipality.Count > 0)
        {
            var ids = labels.Municipality;
            source = source.Where(j => j.MunicipalityConceptId == null || !ids.Contains(j.MunicipalityConceptId));
        }

        if (labels.OccupationGroup.Count > 0)
        {
            var ids = labels.OccupationGroup;
            source = source.Where(j => j.OccupationGroupConceptId == null || !ids.Contains(j.OccupationGroupConceptId));
        }

        if (labels.Occupation.Count > 0)
        {
            var ids = labels.Occupation;
            source = source.Where(j => j.OccupationNameConceptId == null || !ids.Contains(j.OccupationNameConceptId));
        }

        if (labels.EmploymentType.Count > 0)
        {
            var ids = labels.EmploymentType;
            source = source.Where(j => j.EmploymentTypeConceptId == null || !ids.Contains(j.EmploymentTypeConceptId));
        }

        if (labels.WorktimeExtent.Count > 0)
        {
            var ids = labels.WorktimeExtent;
            source = source.Where(j => j.WorktimeExtentConceptId == null || !ids.Contains(j.WorktimeExtentConceptId));
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
        IQueryable<JobAd> source, JobAdSortBy sortBy, ResolvedSearchText text) =>
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
            JobAdSortBy.Relevance => ApplyRelevanceSort(source, text.Positive?.Term),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy), sortBy, "Unknown JobAdSortBy — validator should reject."),
        };

    // ADR 0062 — relevans-sort via PostgreSQL ts_rank(search_vector,
    // websearch_to_tsquery('swedish', q)). Relevans-ordning utan söktext är odefinierad.
    // #831 truth-sync: null-guarden nedan var beskriven som "defense-in-depth" med
    // ListJobAdsQueryValidator som upprätthållare. Den är nu PRIMÄRVÄG för ordinär input.
    // Validatorns `NotEmpty` mäter RÅ q; sorten behöver en non-null RESIDUAL, och sedan
    // q-minimum flyttade till ISearchQueryParser passerar `?q=a&sort=relevans` validatorn,
    // nollas av parsern och landar här med q = null. Fallbacken (PublishedAt desc, kastar
    // ej) är alltså det avsedda beteendet för det fallet, inte en osannolik skyddsnät-gren.
    // Create-invarianten står kvar och gäller SPARADE sökningar, inte den här läsvägen.
    // Rader som matchade enbart via substrängs-fallbackarna (title eller company_name, ej FTS)
    // får ts_rank 0 → de sorteras efter FTS-träffarna, sedan PublishedAt desc, sedan Id.
    private static IQueryable<JobAd> ApplyRelevanceSort(IQueryable<JobAd> source, string? q)
    {
        if (q is null)
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
