namespace Jobbliggaren.Application.Landing.Common;

/// <summary>
/// Publik aggregat-stats för landingpage. ADR 0064 — publik anonym
/// read-aggregat-mönster (pre-computed Redis-cache via Worker-jobb).
/// </summary>
/// <param name="ActiveCount">Antal JobAds med Status=Active (soft-delete-filter applicerat).</param>
/// <param name="NewToday">Antal JobAds med Status=Active OCH PublishedAt &gt;= dagens UTC-midnatt.</param>
/// <param name="IsStale">
/// <c>true</c> om värdet är fallback-floor (cache-miss) eller Worker har inte refreshat på länge.
/// <c>false</c> om värdet skrevs av Worker inom senaste refresh-fönstret.
/// </param>
/// <param name="RefreshedAt">UTC-tidpunkt då Worker beräknade värdet; <c>null</c> vid floor.</param>
public sealed record LandingStatsDto(
    int ActiveCount,
    int NewToday,
    bool IsStale,
    DateTimeOffset? RefreshedAt);
