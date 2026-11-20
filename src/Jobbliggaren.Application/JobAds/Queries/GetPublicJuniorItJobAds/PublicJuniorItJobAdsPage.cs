using Jobbliggaren.Application.Common;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>
/// What the junior IT list answers (ADR 0186): a page, or a page past the list's end (404). A closed union, so the
/// endpoint maps it by type. There is no unavailable arm: the list is read from the database per request, never from
/// the listed-ad snapshot.
/// </summary>
public abstract record PublicJuniorItJobAdsPage
{
    private PublicJuniorItJobAdsPage()
    {
    }

    /// <param name="Rows">The page. <c>TotalCount</c> is the list's size in the chosen kommun, or in all of them.</param>
    /// <param name="Places">Every kommun with a listed junior IT ad, counted over the whole list whatever kommun was
    /// chosen, so the visitor can move between them.</param>
    public sealed record Available(
        PagedResult<PublicJuniorItJobAdDto> Rows,
        IReadOnlyList<PublicJuniorItPlaceDto> Places) : PublicJuniorItJobAdsPage;

    /// <summary>The page lies past the list's last page.</summary>
    public sealed record NoSuchPage : PublicJuniorItJobAdsPage;
}
