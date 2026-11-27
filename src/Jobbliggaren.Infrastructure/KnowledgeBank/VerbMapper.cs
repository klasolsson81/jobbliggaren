using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IVerbMapper"/> over the committed, versioned weak→strong verb mappings, one per CV
/// language (<c>verb-mapping.sv.v1.json</c>, <c>verb-mapping.en.v1.json</c>; F4-7, research §6.3).
/// Loads every asset once at construction and serves the cached immutable contracts. A language
/// without a mapping stops construction, and DI constructs it at registration, so the host never
/// boots without one. The single machine-readable source of the weak-verb lists (DQ8).
/// </summary>
internal sealed class VerbMapper : IVerbMapper
{
    private static readonly IReadOnlyDictionary<ResumeLanguage, KnowledgeBankAsset> Assets =
        new Dictionary<ResumeLanguage, KnowledgeBankAsset>
        {
            [ResumeLanguage.Sv] = new("verb-mapping", "Jobbliggaren.Infrastructure.KnowledgeBank.verb-mapping.sv.v1.json"),
            [ResumeLanguage.En] = new("verb-mapping.en", "Jobbliggaren.Infrastructure.KnowledgeBank.verb-mapping.en.v1.json"),
        };

    private readonly IReadOnlyDictionary<ResumeLanguage, VerbMapping> _mappings;

    public VerbMapper()
        : this(Assets)
    {
    }

    internal VerbMapper(IReadOnlyDictionary<ResumeLanguage, KnowledgeBankAsset> assets) =>
        _mappings = KnowledgeBankAsset.LoadForEveryLanguage(assets, "verb-mapping", Load);

    public VerbMapping GetVerbMapping(ResumeLanguage language) => _mappings[language];

    private static VerbMapping Load(KnowledgeBankAsset asset)
    {
        using var stream = asset.Open();

        var file = JsonSerializer.Deserialize<VerbMappingFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException($"{asset.ResourceName} deserialiserade till null.");

        var groups = file.StrongVerbGroups
            .Select(g => new StrongVerbGroup(g.Group, g.Verbs))
            .ToList();

        var weak = file.WeakVerbs
            .Select(w => new WeakVerbMapping(w.Weak, w.SuggestedStrong, w.Group, w.DropInSafe))
            .ToList();

        return new VerbMapping(
            file.Version, groups, weak, asset.Source, KnowledgeBankTokens.VerbMatch(file.StrongVerbMatch));
    }
}

/// <summary>Deserialisation form for the verb-mapping asset.</summary>
internal sealed record VerbMappingFile
{
    [JsonPropertyName("verbMappingVersion")]
    public string Version { get; init; } = "unknown";

    [JsonPropertyName("strongVerbMatch")]
    public string? StrongVerbMatch { get; init; }

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
        [property: JsonPropertyName("group")] string? Group = null,
        // #494: nullable+defaulted (false) = N-1 tolerance — an older asset without the field
        // maps every weak verb to "not a drop-in", so the transform proposes nothing until the
        // asset opts a pair in. Only same-valency pairs are marked true.
        [property: JsonPropertyName("dropInSafe")] bool DropInSafe = false);
}
