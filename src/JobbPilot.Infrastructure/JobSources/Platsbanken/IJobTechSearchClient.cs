using Refit;

namespace JobbPilot.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Refit-interface mot <c>jobsearch.api.jobtechdev.se</c>. Intern till
/// Infrastructure per ADR 0032 §2 — Application-lagret ser bara
/// <see cref="JobbPilot.Application.JobAds.Abstractions.IJobSource"/>.
/// API-key skickas via DefaultRequestHeaders (DI-konfig i AddJobSources).
/// </summary>
internal interface IJobTechSearchClient
{
    /// <summary>
    /// Sökmotor-endpoint. Default fritext + paginering. Konkret filtrering
    /// (occupation-concept-id, location-concept-id) tillkommer i TD-70.
    /// </summary>
    [Get("/search")]
    Task<JobTechSearchResponse> SearchAsync(
        [Query] string? q = null,
        [Query] int offset = 0,
        [Query] int limit = 100,
        CancellationToken cancellationToken = default);

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
}
