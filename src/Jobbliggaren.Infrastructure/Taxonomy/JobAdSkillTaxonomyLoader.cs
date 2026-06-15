using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// Loads the committed, frozen JobTech skill-taxonomy snapshot
/// (<c>jobad-skill-taxonomy.v30.json</c>) embedded in this assembly (F4-4,
/// ADR 0071/0074 Path C). The deterministic skill vocabulary the
/// <see cref="JobAdKeywordExtractor"/> matches ad terms against.
/// <para>
/// Generated off-build by <c>tools/jobad-skill-taxonomy/generate.mjs</c> from the
/// JobTech Taxonomy v1 GraphQL (taxonomy.api.jobtechdev.se, EPL-2.0 — recorded in
/// THIRD-PARTY-NOTICES). Two layers: AF <c>skill</c> (with its sparse synonym layer
/// — alternative/hidden labels) and the ESCO <c>esco-skill</c> preferred labels.
/// Version-pinned <c>v30</c> to align with <c>taxonomy-snapshot.json</c>. Hermetic
/// build — no build-time fetch, no runtime external hop (ADR 0043 Beslut B).
/// </para>
/// </summary>
internal static class JobAdSkillTaxonomyLoader
{
    // The SAME LogicalName the csproj declares for the embedded resource.
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";

    internal static IReadOnlyList<SkillConcept> Load()
    {
        var asm = typeof(JobAdSkillTaxonomyLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded skill-taxonomy snapshot saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        var file = JsonSerializer.Deserialize<SkillTaxonomyFile>(stream)
            ?? throw new InvalidOperationException(
                "jobad-skill-taxonomy.v30.json deserialiserade till null.");

        return file.Skills;
    }
}

/// <summary>One skill/competence concept (AF <c>skill</c> or ESCO
/// <c>esco-skill</c>) with its surface forms. Infrastructure-internal.</summary>
internal sealed record SkillConcept(
    [property: JsonPropertyName("conceptId")] string ConceptId,
    [property: JsonPropertyName("preferredLabel")] string PreferredLabel,
    [property: JsonPropertyName("layer")] string Layer,
    [property: JsonPropertyName("synonyms")] IReadOnlyList<string> Synonyms);

/// <summary>Deserialisation form for the frozen skill-taxonomy snapshot.</summary>
internal sealed record SkillTaxonomyFile
{
    [JsonPropertyName("taxonomyVersion")]
    public string TaxonomyVersion { get; init; } = "unknown";

    [JsonPropertyName("skills")]
    public IReadOnlyList<SkillConcept> Skills { get; init; } = [];
}
