using System.Text.Json.Serialization;

namespace Jobbliggaren.Infrastructure.Taxonomy;

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

    // ADR 0043-amendment 2026-06-08: kommun nestad under region, yrkesgrupp
    // (ssyk-level-4) nestad under yrkesområde. Nullable med default null →
    // en äldre snapshot utan fälten deserialiseras till null, MapRows tolkar
    // som tom (`?? []`) — bakåtkompat (additivt amendment).
    internal sealed record SnapshotRegion(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("municipalities")] IReadOnlyList<SnapshotMunicipality>? Municipalities = null);

    internal sealed record SnapshotOccupationField(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("occupations")] IReadOnlyList<SnapshotOccupation> Occupations,
        [property: JsonPropertyName("occupationGroups")] IReadOnlyList<SnapshotOccupationGroup>? OccupationGroups = null);

    internal sealed record SnapshotOccupation(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label);

    internal sealed record SnapshotMunicipality(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label);

    internal sealed record SnapshotOccupationGroup(
        [property: JsonPropertyName("conceptId")] string ConceptId,
        [property: JsonPropertyName("label")] string Label);
}
