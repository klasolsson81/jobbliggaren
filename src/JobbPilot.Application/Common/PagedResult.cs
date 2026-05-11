namespace JobbPilot.Application.Common;

/// <summary>
/// Generisk paginerings-DTO. Total separat från Items för att stöd CLAUDE.md §3.6
/// (separat count-query). Items är immutable list för transport-stabilitet.
/// </summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
}
