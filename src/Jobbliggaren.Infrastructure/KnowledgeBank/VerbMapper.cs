using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IVerbMapper"/> over the committed, versioned weak→strong verb mapping
/// (<c>verb-mapping.v1.json</c>, F4-7, research §6.3). Loads + maps the embedded asset
/// once at construction and serves the cached immutable contract — registered as a
/// singleton. The single machine-readable source of the weak-verb list (DQ8).
/// </summary>
internal sealed class VerbMapper : IVerbMapper
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.verb-mapping.v1.json";

    private readonly VerbMapping _mapping = Load();

    public VerbMapping GetVerbMapping() => _mapping;

    private static VerbMapping Load()
    {
        var asm = typeof(VerbMapper).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded verb-mapping-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        var file = JsonSerializer.Deserialize<VerbMappingFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("verb-mapping.v1.json deserialiserade till null.");

        var groups = file.StrongVerbGroups
            .Select(g => new StrongVerbGroup(g.Group, g.Verbs))
            .ToList();

        var weak = file.WeakVerbs
            .Select(w => new WeakVerbMapping(w.Weak, w.SuggestedStrong, w.Group))
            .ToList();

        return new VerbMapping(file.Version, groups, weak);
    }
}

/// <summary>Deserialisation form for the verb-mapping asset.</summary>
internal sealed record VerbMappingFile
{
    [JsonPropertyName("verbMappingVersion")]
    public string Version { get; init; } = "unknown";

    [JsonPropertyName("strongVerbGroups")]
    public IReadOnlyList<StrongVerbGroupFile> StrongVerbGroups { get; init; } = [];

    [JsonPropertyName("weakVerbs")]
    public IReadOnlyList<WeakVerbFile> WeakVerbs { get; init; } = [];

    internal sealed record StrongVerbGroupFile(
        [property: JsonPropertyName("group")] string Group,
        [property: JsonPropertyName("verbs")] IReadOnlyList<string> Verbs);

    internal sealed record WeakVerbFile(
        [property: JsonPropertyName("weak")] string Weak,
        [property: JsonPropertyName("suggestedStrong")] string SuggestedStrong,
        [property: JsonPropertyName("group")] string? Group = null);
}
