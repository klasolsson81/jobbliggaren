using Jobbliggaren.Application.JobAds.Abstractions;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>#2199 — the <see cref="IOccupationNameLineageProvider"/> over the lineage loaded at host start.</summary>
internal sealed class OccupationNameLineageProvider(OccupationNameLineage lineage) : IOccupationNameLineageProvider
{
    public OccupationNameLineage Lineage { get; } = lineage ?? throw new ArgumentNullException(nameof(lineage));
}
