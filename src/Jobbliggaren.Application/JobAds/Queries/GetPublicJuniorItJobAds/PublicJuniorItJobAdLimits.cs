using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>The junior IT list's page size, its depth cap and the two orders it offers (ADR 0186).</summary>
public static class PublicJuniorItJobAdLimits
{
    public const int PageSize = 20;

    /// <summary>Bounds the URL space, not the list: a page past the list's end is a 404 before this is reached.</summary>
    public const int MaxPage = 100;

    /// <summary>The longest JobTech concept id accepted as a kommun; real ids are twelve characters.</summary>
    public const int MaxMunicipalityLength = 32;

    /// <summary>Newest first, or the soonest last publication day first, as on <c>/jobb</c>.</summary>
    public static readonly IReadOnlyList<JobAdSortBy> Orders = [JobAdSortBy.PublishedAtDesc, JobAdSortBy.ExpiresAtAsc];
}
