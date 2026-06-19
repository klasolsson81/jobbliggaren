using Jobbliggaren.Application.Common;
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
    string? Q = null,
    // ADR 0042 Beslut E — "Ny sedan"-fönster (runtime-kontext, ej i
    // SearchCriteria; analog Page/PageSize). Driver JobAdDto.IsNew.
    DateTimeOffset? Since = null,
    // ADR 0060 amendment 2026-06-12 (Fas E2j) — commit-intent-gate för
    // auto-capture. Default false: live-förhandsvisning (router.replace per
    // ord) fångas ej; FE sätter ?commit=1 vid Enter/Sök/förslags-val/toolbar.
    // record-property matchar ICapturesRecentSearch.Commit automatiskt
    // (paritet Since/Page). Påverkar ENDAST capture-behaviorns no-op-gate —
    // ingår inte i SearchCriteria/filter-identiteten.
    bool Commit = false) : IQuery<PagedResult<JobAdDto>>, ICapturesRecentSearch
{
    // ICapturesRecentSearch + default/fallback-väg ser ALLTID ett rent
    // Domain-värde (MatchDesc → PublishedAtDesc). Match-sorten persisteras aldrig
    // som en anonym recent-search-/SavedSearch-sort (ADR 0076 Decision 5).
    public JobAdSortBy SortBy => Sort.ToDomainSort();

    // F4-14 — match-sort-intentet. Driver handlerns gren till den per-användar-
    // match-sort-porten (med honest PublishedAtDesc-fallback, Decision 7).
    public bool SortByMatch => Sort == ListJobAdsSort.MatchDesc;
}
