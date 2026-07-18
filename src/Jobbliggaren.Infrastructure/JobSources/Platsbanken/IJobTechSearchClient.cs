using Refit;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Refit-interface mot <c>jobsearch.api.jobtechdev.se</c>. Intern till
/// Infrastructure per ADR 0032 §2 — Application-lagret ser bara
/// <see cref="Jobbliggaren.Application.JobAds.Abstractions.IJobSource"/>.
/// API-key skickas via DefaultRequestHeaders (DI-konfig i AddJobSources).
/// </summary>
internal interface IJobTechSearchClient
{
    /// <summary>
    /// Per-ID-fetch mot <c>jobsearch.api.jobtechdev.se/ad/{id}</c>. Använd av
    /// <c>BackfillJobAdSsykJob</c> för att re-hämta enskilda annonser vars
    /// raw_payload importerades före 2026-05-20-`JobTechHit.Occupation`-fixen
    /// (snapshot-trunkering når dem inte — ADR 0032-amendment 2026-05-16
    /// bounded retry).
    /// <para>
    /// Nullable return: Refit deserialiserar 404 → null på interface med
    /// nullable Task-shape (verifierat mot Refit 8+ default-konfig). 404
    /// betyder "annons borttagen från JobTech källan" — backfill-callern
    /// hanterar som "skip+count", INTE som arkivering (ADR 0032-amendment
    /// 2026-05-23 retention-disciplin bevaras separat).
    /// </para>
    /// </summary>
    [Get("/ad/{id}")]
    Task<JobTechHit?> GetAdByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// #551 — the remote/distans harvest. <c>jobsearch.api.jobtechdev.se/search?remote=true</c>
    /// returns the ads AF classifies as remote (the same classification that powers Platsbanken's own
    /// remote filter). The ad RESPONSE schema has no per-ad remote field (ADR 0067 Beslut 3, amended
    /// 2026-07-18) — <c>remote</c> is a server-side query parameter only — so <see cref="PlatsbankenJobSource"/>
    /// paginates this once per snapshot run to build the set of remote source-ids, then sets
    /// <c>JobAdFacets.Remote</c> from set membership. Paginated with <paramref name="offset"/>/<paramref name="limit"/>
    /// (JobSearch caps offset at 2000; the remote total is well under that). Failure is handled by the
    /// caller's fail-safe gate (a failed harvest leaves the remote column untouched — never flips the
    /// corpus to false).
    /// </summary>
    [Get("/search?remote=true")]
    Task<JobTechSearchListResponse> SearchRemoteAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
