using System.Text.Json;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// The committed <c>taxonomy-snapshot.json</c>, read from the embedded resource. It has two production readers: the
/// seeder, which writes it into <c>taxonomy_concepts</c> at Api start, and since #2199 PR 2b the composition root,
/// which builds the religious occupations' field sets from it in both hosts (parity with
/// <see cref="OccupationNameLineageLoader"/>).
/// </summary>
internal static class TaxonomySnapshotLoader
{
    private const string ResourceName = "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    internal static TaxonomySnapshotFile Load()
    {
        var asm = typeof(TaxonomySnapshotLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded taxonomi-snapshot saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
        return JsonSerializer.Deserialize<TaxonomySnapshotFile>(stream)
            ?? throw new InvalidOperationException(
                "taxonomy-snapshot.json deserialiserade till null.");
    }
}
