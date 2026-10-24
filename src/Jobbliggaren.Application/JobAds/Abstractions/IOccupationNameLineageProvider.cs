namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// #2199 — the occupation-name lineage, loaded once at host start from the embedded artefact. Only the
/// search ports' text resolver reads it, so the filter and the counts share one reader.
/// </summary>
public interface IOccupationNameLineageProvider
{
    OccupationNameLineage Lineage { get; }
}
