using System.Text.Json;

namespace Jobbliggaren.Infrastructure.Taxonomy;

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
