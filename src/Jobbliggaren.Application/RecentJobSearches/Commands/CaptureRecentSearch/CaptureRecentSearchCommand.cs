using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.CaptureRecentSearch;

/// <summary>
/// #2285 (ADR 0060 Amendment 2026-11-22) — stores a search the user ran on purpose in their recent searches, and is the
/// only way one reaches the table: no query stores a search, so no GET can. It carries the search's identity axes and
/// nothing else. The user is <see cref="ICurrentUser"/>'s, never the body's, and <see cref="RecentSearchCaptureGate"/>
/// decides what may be stored, as it does for the list's verdict.
/// <para>
/// Not auditable: a recent search has no audit-trail value (ADR 0060 Beslut 6), and a row per deliberate search would
/// grow <c>audit_log</c> with no accountability gained.
/// </para>
/// </summary>
public sealed record CaptureRecentSearchCommand(
    string? Q = null,
    IReadOnlyList<string>? OccupationGroup = null,
    IReadOnlyList<string>? OccupationName = null,
    IReadOnlyList<string>? Municipality = null,
    IReadOnlyList<string>? Region = null,
    IReadOnlyList<string>? EmploymentType = null,
    IReadOnlyList<string>? WorktimeExtent = null,
    IReadOnlyList<string>? Employer = null,
    bool Remote = false,
    bool JuniorIt = false,
    ListJobAdsSort Sort = ListJobAdsSort.PublishedAtDesc)
    : ICommand<Result<CaptureRecentSearchResult>>, IAuthenticatedRequest, IRecentSearchCandidate
{
    // The list's own mapping: a match sort has no anonymous meaning and is stored as newest first (ADR 0076 Decision 5).
    public JobAdSortBy SortBy => Sort.ToDomainSort();
}
