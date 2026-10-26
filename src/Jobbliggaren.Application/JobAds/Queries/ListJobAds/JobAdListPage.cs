using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.RecentJobSearches.Common;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

/// <summary>
/// #2199 PR 2b — the /jobb list page: a <see cref="PagedResult{T}"/> of ads and whether
/// <see cref="RecentSearchCaptureGate"/> admits the search into recent searches, so the hero offers
/// "Spara sökningen" only when a commit would store it. It is flat on the wire, the page's members plus one
/// field, so a web image older than the API still parses it.
/// </summary>
/// <remarks>
/// It delegates to the page it wraps, which keeps one set of invariants and one <c>TotalPages</c>. It
/// implements <see cref="IRecentSearchCaptureResponse"/> because the capture behaviour is keyed on the
/// response type and does nothing, silently, for a response without it. <c>PagedResultContractTests</c>
/// declares it as the fourth form.
/// </remarks>
public sealed record JobAdListPage : IRecentSearchCaptureResponse
{
    private readonly PagedResult<JobAdDto> _page;

    public JobAdListPage(PagedResult<JobAdDto> page, bool isStorableAsRecentSearch)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
        IsStorableAsRecentSearch = isStorableAsRecentSearch;
    }

    public IReadOnlyList<JobAdDto> Items => _page.Items;
    public int TotalCount => _page.TotalCount;
    public int Page => _page.Page;
    public int PageSize => _page.PageSize;
    public int TotalPages => _page.TotalPages;

    /// <summary>Whether a committed run of this search would be stored in recent searches.</summary>
    public bool IsStorableAsRecentSearch { get; }
}
