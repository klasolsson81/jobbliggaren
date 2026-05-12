namespace JobbPilot.Infrastructure.JobSources.Platsbanken;

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
    /// Hämtar <c>/snapshot</c> — fullständig lista över aktiva annonser. Använt
    /// av nattlig backfill (P8c) och admin-trigger (P8b).
    /// </summary>
    Task<IReadOnlyList<JobTechHit>> FetchSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Hämtar <c>/stream?date=ISO8601</c> — alla annons-changes sedan timestamp.
    /// Returnerar diskriminerade events (upsert vs removal) via
    /// <c>JobTechHit.Removed == true</c>.
    /// </summary>
    IAsyncEnumerable<JobTechHit> StreamChangesAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken);
}
