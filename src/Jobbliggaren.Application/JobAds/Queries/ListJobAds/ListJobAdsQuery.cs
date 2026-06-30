using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

// ADR 0042 Beslut B — multi-värde-listor (IReadOnlyList). Nullable behålls
// för "ej angivet" (handler översätter null → tom lista innan filter-SPOT:en).
// Page/PageSize/SortBy/Q oförändrade.
// ADR 0060 — ICapturesRecentSearch markerar queryn för auto-capture-behavior
// (record-properties matchar interface-shape automatiskt).
//
// ADR 0067 Beslut 1 (Platsbanken sök-paritet Fas C2, CTO-dom (e) 2026-06-09):
// Ssyk-paramen (occupation-name) är BORTTAGEN — no-op sedan C1, och C2
// upplöste persistens-bindningen (VO-/entity-expansion + reverse-lookup-
// migration). FE:s ?ssyk= ignoreras som obunden query-param (200 OK) tills
// Fas E byter picker till ?occupationGroup=.
// ADR 0076 Decision 4/5 (F4-14) — Sort är read-side sort-ytan (ListJobAdsSort):
// de fem rena JobAdSortBy-värdena + MatchDesc ("Sortera efter matchning").
// Domän-enumen JobAdSortBy hålls match-ren (CTO-bind D2=Y); SortBy/SortByMatch
// härleds nedan så ICapturesRecentSearch/FilterHash bara ser rena värden.
public sealed record ListJobAdsQuery(
    int Page = 1,
    int PageSize = 20,
    ListJobAdsSort Sort = ListJobAdsSort.PublishedAtDesc,
    IReadOnlyList<string>? OccupationGroup = null,
    IReadOnlyList<string>? Municipality = null,
    IReadOnlyList<string>? Region = null,
    // ADR 0067 Beslut 6 (Fas B2, 2026-06-12) — Klass 2 anställningsform +
    // omfattning. Bunds från ?employmentType=/?worktimeExtent=; ortogonala
    // IN-filter (ej geo-union). Matchar ICapturesRecentSearch automatiskt.
    IReadOnlyList<string>? EmploymentType = null,
    IReadOnlyList<string>? WorktimeExtent = null,
    // #311 D6 (följ arbetsgivare, ADR 0087) — arbetsgivar-facet på org.nr (den
    // KANONISKA nyckeln; ingen fuzzy namn-matchning). Ortogonal IN-equality-dimension
    // (speglar EmploymentType/WorktimeExtent). Bunds från ?employer=; upprepad
    // query-string binds till string[].
    // CONTAINED-scope (CTO-bind 2026-06-30): Employer är en ÄKTA persisterad
    // sök-dimension (ADR 0087 D6), MEN PR-2 trådar den ENDAST in i live-sök-vägen.
    // Den ingår därför INTE i ICapturesRecentSearch (interfacet listar inte Employer
    // → capture-behaviorn ser den aldrig) och INTE i SearchCriteria/SavedSearch i
    // PR-2. Detta skiljer Employer från runtime-kontext-flaggorna nedan (MatchGrades/
    // IncludeRelated/status), som ALDRIG persisteras by design: Employer SKA
    // persisteras, men den threadingen (VO + RecentJobSearch-kolumn + FilterHash +
    // SavedSearch jsonb) är sekvenserad till PR-2b (landar med #408 FE-konsumenten).
    IReadOnlyList<string>? Employer = null,
    string? Q = null,
    // ADR 0060 amendment 2026-06-12 (Fas E2j) — commit-intent-gate för
    // auto-capture. Default false: live-förhandsvisning (router.replace per
    // ord) fångas ej; FE sätter ?commit=1 vid Enter/Sök/förslags-val/toolbar.
    // record-property matchar ICapturesRecentSearch.Commit automatiskt
    // (paritet Since/Page). Påverkar ENDAST capture-behaviorns no-op-gate —
    // ingår inte i SearchCriteria/filter-identiteten.
    bool Commit = false,
    // ADR 0079 STEG 5 (grad-filter, 2026-06-23) — den per-användar-grad-filtreringen
    // ("Matchning"-toggeln). RUNTIME-KONTEXT (analog Since/Commit): den ingår ALDRIG
    // i ICapturesRecentSearch / SearchCriteria / FilterHashCalculator (de läser bara
    // de namngivna fälten ovan) — en per-användar-grad får aldrig förorena den anonyma
    // sök-identiteten (ADR 0039 Beslut 1 / ADR 0062-isolering). En icke-tom lista
    // betyder "Matchning PÅ, visa endast dessa grader"; tom/null = Matchning AV
    // (Klas-val 2026-06-23: av = noll grader). Endast Fast-bandet (Grund/Bra/Stark) är
    // filtrerbart — Topp kan inte beräknas i SQL (G3-OPT-A); validatorn avvisar Top.
    IReadOnlyList<MatchGrade>? MatchGrades = null,
    // #300 PR-5a (ADR 0084 §A — "Visa relaterade också"-toggeln, off by default) — när true
    // breddar profil-byggaren yrkes-gaten exakt → exakt ∪ related så related-annonser kan tagga
    // MatchGrade.Related (rank 2). RUNTIME-KONTEXT (paritet MatchGrades/Commit): ingår
    // ALDRIG i ICapturesRecentSearch / SearchCriteria / FilterHashCalculator (de läser bara de
    // namngivna sök-identitets-fälten ovan) — en per-användar-breddning får aldrig förorena den
    // anonyma sök-identiteten (ADR 0039 Beslut 1 / ADR 0062). Default false = beteende-inert
    // (exakt-only, dagens beteende). Det publika /jobb-rutt-värdet ?relaterade=on mappas hit av
    // FE (PR-5b); API-kontraktet bär den engelska flaggan.
    bool IncludeRelated = false,
    // #383 (CTO-bind cto-7f3a9c2e1b4d8a6f, Approach B) — den per-användar-status-
    // filtreringen ("Sparade"/"Ansökta"/"Dölj ansökta"-facetterna). RUNTIME-KONTEXT
    // (paritet MatchGrades/IncludeRelated): de ingår ALDRIG i ICapturesRecentSearch /
    // SearchCriteria / FilterHashCalculator (de läser bara de namngivna sök-identitets-
    // fälten ovan — de tre nya bool:arna exkluderas automatiskt) — en personlig
    // status-vy får aldrig förorena den anonyma, cachebara sök-identiteten. Bunds från
    // ?savedOnly=/?appliedOnly=/?hideApplied=; FE mappar de svenska rutt-värdena
    // ?sparade=on/?ansokta=on/?doljAnsokta=on hit (API-kontraktet bär engelska flaggor,
    // paritet relaterade→includeRelated). savedOnly ∨ appliedOnly = OR (union);
    // appliedOnly ∧ hideApplied = 400 (validator-mutex — självmotsägande).
    bool SavedOnly = false,
    bool AppliedOnly = false,
    bool HideApplied = false,
    // #419 punkt 1 (CTO Approach A, 2026-06-30) — "Visa bara matchade": visa ENDAST
    // annonser med en positiv matchningsgrad för användaren (rank > 0 = SSYK ∈ exakt ∪
    // related), oavsett vilken specifik grad. RUNTIME-KONTEXT (paritet MatchGrades/
    // IncludeRelated/status): ingår ALDRIG i ICapturesRecentSearch / SearchCriteria /
    // FilterHashCalculator (de läser bara de namngivna sök-identitets-fälten) — en
    // per-användar-vy får aldrig förorena den anonyma, cachebara sök-identiteten (ADR
    // 0039 Beslut 1 / 0062). Bunds från ?onlyMatched=; FE mappar den svenska sentinel-
    // paramen ?baraMatchade=on hit. Handlern implementerar den genom att injicera HELA
    // det filtrerbara Fast-bandet (Grund/Relaterat/Bra/Stark = rank {1,2,3,4}) NÄR ingen
    // specifik grad-delmängd är vald → återbrukar det BEFINTLIGA positiv-only grad-WHERE:t
    // verbatim (DRY; ingen ny rank>0-Expression, ingen ny port-param). En specifik
    // grad-delmängd VINNER (only-matched = det tomma-delmängds-fallet). Default false.
    bool OnlyMatched = false)
    : IQuery<PagedResult<JobAdDto>>, ICapturesRecentSearch
{
    // ICapturesRecentSearch + default/fallback-väg ser ALLTID ett rent
    // Domain-värde (MatchDesc → PublishedAtDesc). Match-sorten persisteras aldrig
    // som en anonym recent-search-/SavedSearch-sort (ADR 0076 Decision 5).
    public JobAdSortBy SortBy => Sort.ToDomainSort();

    // F4-14 — match-sort-intentet. Driver handlerns gren till den per-användar-
    // sökvägen (med honest PublishedAtDesc-fallback, Decision 7). MatchDesc =
    // "ordna på match-rank"; det gatar INTE längre per-användar-vägen ensamt
    // (ADR 0079 STEG 5 re-bind — grad-filter + valfri sort frikopplades).
    public bool SortByMatch => Sort == ListJobAdsSort.MatchDesc;

    // ADR 0079 STEG 5 — "Matchning"-kontext aktiv ⇔ minst en grad vald (Klas-val:
    // av = noll grader). Detta — INTE sort-värdet — är toggle-signalen som driver
    // per-användar-vägen (där grad-WHERE + den valda sorten appliceras). MatchDesc
    // utan grader (case 3) faller fortfarande in på per-användar-vägen via
    // SortByMatch (match-rank-ordning utan grad-filter, otaggade sist).
    public bool MatchContextActive => MatchGrades is { Count: > 0 };

    // #383 — den per-användar-status-predikaten samlad. Aktiv ⇔ minst en facett vald;
    // driver handlerns seeker-resolution + status-gren (frikopplad från match-gaten).
    public JobAdStatusFilter Status => new(SavedOnly, AppliedOnly, HideApplied);
}
