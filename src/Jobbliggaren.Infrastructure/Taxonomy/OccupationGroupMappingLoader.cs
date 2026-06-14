using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Loads the committed, frozen occupation-name→ssyk-level-4 mapping
/// (<c>occupation-name-to-ssyk-level-4.v30.json</c>) embedded in this assembly
/// (F4-3, ADR 0040 amendment).
/// <para>
/// The resource is a FROZEN, migration-owned artefact (ADR 0067 Beslut 1, the C2
/// reverse-lookup — documented "regenereras ALDRIG" for migration immutability).
/// F4-3 reads it from the live read path as a second, <b>read-only</b> consumer:
/// the bytes are never regenerated, so migration immutability is fully preserved
/// (senior-cto-advisor Decision 2, 2026-06-14 — the role-widening from
/// "migration-owned" to "shared reference data" is RATIFIED and recorded as an
/// ADR 0043 amendment-note). This loader owns its OWN copy of the resource
/// LogicalName const so the deriver has no compile dependency on the migration
/// type.
/// </para>
/// <para>
/// Version alignment: the map is <c>taxonomyVersion 30</c>, matching the committed
/// <c>taxonomy-snapshot.json</c> (v30). A future snapshot bump must revisit F4-3
/// (the frozen map stays v30 by design); coverage drift would degrade gracefully
/// to manual selection.
/// </para>
/// </summary>
internal static class OccupationGroupMappingLoader
{
    // The SAME LogicalName the csproj declares for the embedded resource.
    // Duplicated here deliberately (a single const, with this comment) so the
    // deriver never references the C2 migration class (CTO Decision 2 /
    // dotnet-architect §3.3).
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    /// <summary>
    /// occupation-name concept-id → ssyk-level-4 (yrkesgrupp) concept-id. Ordinal
    /// keys (concept-ids are case-sensitive tokens).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Load()
    {
        var asm = typeof(OccupationGroupMappingLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded frozen occupation-group map saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        var file = JsonSerializer.Deserialize<OccupationGroupMappingFile>(stream)
            ?? throw new InvalidOperationException(
                "occupation-name-to-ssyk-level-4.v30.json deserialiserade till null.");

        return file.Mappings;
    }
}

/// <summary>
/// Deserialisation form for the frozen occupation-name→ssyk-4 map (ADR 0067 C2).
/// Infrastructure-internal; not a Domain/Application type.
/// </summary>
internal sealed record OccupationGroupMappingFile
{
    [JsonPropertyName("taxonomyVersion")]
    public string TaxonomyVersion { get; init; } = "unknown";

    [JsonPropertyName("mappings")]
    public Dictionary<string, string> Mappings { get; init; } = new();
}
