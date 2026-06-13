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
public sealed record ListJobAdsQuery(
    int Page = 1,
    int PageSize = 20,
    JobAdSortBy SortBy = JobAdSortBy.PublishedAtDesc,
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
    bool Commit = false) : IQuery<PagedResult<JobAdDto>>, ICapturesRecentSearch;
