using System.Text.Json.Serialization;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0084 — deserialization form for the committed
/// <c>occupation-substitutability.json</c> (embedded resource). Off-search-path,
/// one-shot generated from JobTech Taxonomy GraphQL (<c>substitutes</c>,
/// occupation-name) rolled up to ssyk-4 → ssyk-4 via the frozen
/// <c>occupation-name-to-ssyk-level-4.v30.json</c> map (Variant A: manual
/// regeneration + commit, <c>generate-substitutability.mjs</c>). Not a Domain or
/// Application type. Parity <see cref="TaxonomySnapshotFile"/> /
/// <c>Klass2TaxonomyFile</c>.
/// </summary>
internal sealed record OccupationSubstitutabilityFile
{
    /// <summary>Bumped on every regeneration → forces a re-seed (combined with the
    /// snapshot + Klass 2 versions in the seeder's idempotency key).</summary>
    [JsonPropertyName("substitutabilityVersion")]
    public string Version { get; init; } = "unknown";

    /// <summary>The relation kind for every edge in the file (v1 = one kind per
    /// file). Mapped to <see cref="TaxonomyRelationKind"/> by the seeder. Defaults
    /// to the <c>"unknown"</c> sentinel (parity <see cref="Version"/>) so a missing
    /// key fails loud in <c>MapRelationKind</c> rather than silently assuming a
    /// kind.</summary>
    [JsonPropertyName("relationKind")]
    public string RelationKind { get; init; } = "unknown";

    [JsonPropertyName("relations")]
    public IReadOnlyList<SubstitutabilityRelation> Relations { get; init; } = [];

    /// <summary>A source group and its related ssyk-4 groups (outgoing edges).</summary>
    internal sealed record SubstitutabilityRelation(
        [property: JsonPropertyName("sourceConceptId")] string SourceConceptId,
        [property: JsonPropertyName("relatedConceptIds")] IReadOnlyList<string> RelatedConceptIds);
}
