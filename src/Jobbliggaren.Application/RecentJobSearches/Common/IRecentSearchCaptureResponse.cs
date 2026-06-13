namespace Jobbliggaren.Application.RecentJobSearches.Common;

/// <summary>
/// Response-side markör (ADR 0060) — query-response som kan ge totalCount för
/// capture. Implementeras av <c>PagedResult&lt;T&gt;</c>. Tillåter generiskt
/// behavior att läsa TotalCount typat utan att veta om JobAdDto-konkret type
/// (open/closed, Martin 2017 kap. 8).
/// </summary>
public interface IRecentSearchCaptureResponse
{
    int TotalCount { get; }
}
