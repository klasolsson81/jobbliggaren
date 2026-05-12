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
}
