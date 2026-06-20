using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Queries.ListJobAds;

/// <summary>
/// F4-14 (ADR 0076 Decision 4/5) — den read-side sort-ytan för <c>/jobb</c>.
/// Application-nivå: de fem rena <see cref="JobAdSortBy"/>-värdena (anonyma,
/// cachebara, SPOT-delade med SavedSearch/recent-search) PLUS
/// <see cref="MatchDesc"/> — den per-användar-match-sorten.
/// <para>
/// <b>Varför en separat enum (CTO-bind D2=Y 2026-06-19):</b> Domän-enumen
/// <see cref="JobAdSortBy"/> delas av tre match-blinda konsumenter —
/// <c>JobAdSearchQuery.ApplySort</c> (kan inte beräkna match: ingen profil),
/// <c>SearchCriteria</c> (persisterad SavedSearch-identitet) och
/// <c>FilterHashCalculator</c> (recent-search dedup-hash). Att lägga ett
/// match-värde där hade förorenat den SPOT:en (Decision 5) och gett
/// <c>ApplySort</c> ett ovärderbart värde på RunSavedSearch-vägen. Match-sorten
/// är en Application+Api-concern; den når aldrig Domain.
/// </para>
/// </summary>
public enum ListJobAdsSort
{
    PublishedAtDesc = 0,
    PublishedAtAsc = 1,
    ExpiresAtDesc = 2,
    ExpiresAtAsc = 3,
    Relevance = 4,

    // F4-14 — "Sortera efter matchning" (grad fallande, tie-break publishedAt
    // fallande). Application-only; mappas ALDRIG till ett Domain-värde (se
    // ToDomainSort) — match-sorten har ingen anonym, persisterbar motsvarighet.
    MatchDesc = 5,
}

public static class ListJobAdsSortExtensions
{
    /// <summary>
    /// Den rena <see cref="JobAdSortBy"/> som driver default-/fallback-vägen och
    /// recent-search-capturen. <see cref="ListJobAdsSort.MatchDesc"/> mappar till
    /// <see cref="JobAdSortBy.PublishedAtDesc"/>: det är både den honesta fallbacken
    /// (ingen angiven yrkespreferens → Decision 7) och det värde som
    /// recent-search-hashen lagrar (match-sort är per-användare, aldrig en anonym
    /// SavedSearch/recent-search-intention).
    /// </summary>
    public static JobAdSortBy ToDomainSort(this ListJobAdsSort sort) => sort switch
    {
        ListJobAdsSort.PublishedAtDesc => JobAdSortBy.PublishedAtDesc,
        ListJobAdsSort.PublishedAtAsc => JobAdSortBy.PublishedAtAsc,
        ListJobAdsSort.ExpiresAtDesc => JobAdSortBy.ExpiresAtDesc,
        ListJobAdsSort.ExpiresAtAsc => JobAdSortBy.ExpiresAtAsc,
        ListJobAdsSort.Relevance => JobAdSortBy.Relevance,
        ListJobAdsSort.MatchDesc => JobAdSortBy.PublishedAtDesc,
        _ => throw new ArgumentOutOfRangeException(
            nameof(sort), sort, "Unknown ListJobAdsSort — validator should reject."),
    };
}
