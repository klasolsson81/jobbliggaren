using System.Text.Json.Serialization;

namespace JobbPilot.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 — deserialiserings-form för den committade
/// <c>taxonomy-snapshot.json</c> (embedded resource). Off-search-path
/// engångs-genererad från JobTech Taxonomy API (Variant A: manuell
/// regenerering + commit). Inte en Domain-/Application-typ.
/// </summary>
internal sealed record TaxonomySnapshotFile
{
    [JsonPropertyName("taxonomyVersion")]
    public string TaxonomyVersion { get; init; } = "unknown";

    [JsonPropertyName("regions")]
    public IReadOnlyList<SnapshotRegion> Regions { get; init; } = [];

    [JsonPropertyName("occupationFields")]
    public IReadOnlyList<SnapshotOccupationField> OccupationFields { get; init; } = [];

    internal sealed record SnapshotRegion(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label);

    internal sealed record SnapshotOccupationField(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("occupations")] IReadOnlyList<SnapshotOccupation> Occupations);

    internal sealed record SnapshotOccupation(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label);
}
