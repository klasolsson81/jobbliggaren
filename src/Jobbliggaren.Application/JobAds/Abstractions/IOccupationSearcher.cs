namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// #2198 (ADR 0180) — the occupation pickers' free-text search: a typed word is matched against
/// every mapped occupation-name and every ssyk-level-4 group label, and the hits come back as
/// structured rows. The user CHOOSES here, so the deriver's spread gate does not apply; the CV
/// proposal keeps it (<see cref="IOccupationCodeDeriver"/>, ADR 0040 Beslut 4). Both read the same
/// occupation-name index. Deterministic, no AI (ADR 0071), persists nothing, never logs the query.
/// </summary>
public interface IOccupationSearcher
{
    /// <summary>
    /// The rows for <paramref name="query"/>, ordered and capped by the searcher, which is the only
    /// ordering authority. A query with no token of at least two characters gives no rows.
    /// </summary>
    ValueTask<OccupationSearchResult> SearchAsync(string query, CancellationToken cancellationToken);
}
