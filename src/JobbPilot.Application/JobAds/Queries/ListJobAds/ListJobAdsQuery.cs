using JobbPilot.Application.Common;
using JobbPilot.Application.RecentJobSearches.Common;
using JobbPilot.Domain.JobAds;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

// ADR 0042 Beslut B — Ssyk/Region single→multi (IReadOnlyList). Nullable
// behålls för "ej angivet" (handler översätter null → tom lista innan
// JobAdSearch.ApplyCriteria). Page/PageSize/SortBy/Q oförändrade.
// ADR 0060 — ICapturesRecentSearch markerar queryn för auto-capture-behavior
// (record-properties matchar interface-shape automatiskt).
//
// ADR 0067 Beslut 1 (Platsbanken sök-paritet Fas C1, Variant C): nya
// dimensioner OccupationGroup (ssyk-level-4/yrkesgrupp — primärt yrke-filter)
// + Municipality (kommun). Ssyk (occupation-name) BEHÅLLS som deprecerad
// no-op-param: ApplyCriteria ignorerar dess equality, men fältet behövs för
// ICapturesRecentSearch-shape (RecentJobSearch-fångst) tills Fas C2 expanderar
// VO:t/entiteten. FE byter ?ssyk= → ?occupationGroup= i Fas E.
public sealed record ListJobAdsQuery(
    int Page = 1,
    int PageSize = 20,
    JobAdSortBy SortBy = JobAdSortBy.PublishedAtDesc,
    IReadOnlyList<string>? OccupationGroup = null,
    IReadOnlyList<string>? Municipality = null,
    IReadOnlyList<string>? Region = null,
    IReadOnlyList<string>? Ssyk = null,
    string? Q = null,
    // ADR 0042 Beslut E — "Ny sedan"-fönster (runtime-kontext, ej i
    // SearchCriteria; analog Page/PageSize). Driver JobAdDto.IsNew.
    DateTimeOffset? Since = null) : IQuery<PagedResult<JobAdDto>>, ICapturesRecentSearch;
