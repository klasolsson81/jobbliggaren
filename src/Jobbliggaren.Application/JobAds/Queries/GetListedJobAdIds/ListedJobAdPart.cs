namespace Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;

/// <summary>
/// What the sitemap query answers (ADR 0174). A closed union, so the endpoint maps it by type. An unavailable snapshot
/// is not an <c>ErrorKind</c>: no rule about the request failed, the data is not fresh enough to answer.
/// </summary>
public abstract record ListedJobAdPart
{
    private ListedJobAdPart()
    {
    }

    /// <summary>The part's ids in id order. An empty list is a measured zero.</summary>
    public sealed record Available(IReadOnlyList<Guid> Ids) : ListedJobAdPart;

    /// <summary>No read has succeeded yet, or the newest snapshot is outside its bound.</summary>
    public sealed record Unavailable : ListedJobAdPart;
}
