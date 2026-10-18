using Jobbliggaren.Application.JobAds.Listing;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// The catalogue's page size and depth cap (ADR 0176 F2). The cap is derived from the snapshot's ceiling, never picked:
/// it bounds the URL space and never the listed set, because past the ceiling the snapshot publishes nothing at all.
/// <c>PublicJobAdCatalogueLimitsTests</c> pins both edges.
/// </summary>
public static class PublicJobAdCatalogueLimits
{
    public const int PageSize = 20;

    public const int MaxPage = (ListedJobAdSnapshotLimits.Ceiling + PageSize - 1) / PageSize;
}
