using Jobbliggaren.Application.Common;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// What the catalogue query answers (ADR 0176). A closed union, so the endpoint maps it by type: besides a page there are
/// two answers that are not a page, a page past the end (404) and a snapshot that is not fresh enough to answer (503).
/// Neither is an <c>ErrorKind</c>: no rule about the request failed.
/// </summary>
public abstract record PublicJobAdCataloguePage
{
    private PublicJobAdCataloguePage()
    {
    }

    /// <summary>One page, newest first. On an empty corpus page 1 is available and empty, a measured zero.</summary>
    public sealed record Available(PagedResult<PublicJobAdSummaryDto> Rows) : PublicJobAdCataloguePage;

    /// <summary>The snapshot is fresh, and the page lies past its last page.</summary>
    public sealed record NoSuchPage : PublicJobAdCataloguePage;

    /// <summary>No read has succeeded yet, or the newest snapshot is outside its bound.</summary>
    public sealed record Unavailable : PublicJobAdCataloguePage;
}
