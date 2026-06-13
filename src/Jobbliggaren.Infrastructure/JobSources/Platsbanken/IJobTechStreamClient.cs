namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Typed-client mot <c>jobstream.api.jobtechdev.se</c>. NDJSON long-polling-
/// stream + snapshot. Refit används medvetet INTE här eftersom Stream:s
/// polymorfa event-schema (<c>{...}</c> vs <c>{..., "removed": true, ...}</c>)
/// förlorar type-safety via Refit. Per-line <c>JsonDocument</c>-parsing ger
/// explicit kontroll. ADR 0032 §2.
/// </summary>
internal interface IJobTechStreamClient
{
    /// <summary>
    /// Strömmar <c>/v2/snapshot</c> — fullständig set av aktiva annonser (~300 MB,
    /// web-verifierat 2026-05-16). <see cref="IAsyncEnumerable{T}"/> via
    /// <c>DeserializeAsyncEnumerable</c> så hela arrayen aldrig materialiseras
    /// (root-cause-fix 2026-05-16 — tidigare <c>List&lt;&gt;</c> OOM:ade Fargate).
    /// </summary>
    IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Hämtar <c>/stream?date=ISO8601</c> — alla annons-changes sedan timestamp.
    /// Returnerar diskriminerade events (upsert vs removal) via
    /// <c>JobTechHit.Removed == true</c>.
    /// </summary>
    IAsyncEnumerable<JobTechHit> StreamChangesAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken);
}
