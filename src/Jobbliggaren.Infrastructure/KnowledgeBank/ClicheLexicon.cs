using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IClicheLexicon"/> over the committed, versioned CV lexicons, one per CV language
/// (<c>cliche-list.sv.v2.json</c>, <c>cliche-list.en.v1.json</c>; F4-7, research §6.1). Loads and
/// maps every asset once at construction and serves the cached immutable contracts. A language
/// without a list stops construction, and DI constructs it at registration, so the host never boots
/// without one. v2 (#490/#495/#496): every entry carries a <c>kind</c> discriminator, an advisory
/// <c>guidance</c> field, and an OPTIONAL genuine <c>dropInReplacement</c> (the only literal the
/// propose step may apply verbatim — no synthesis).
/// </summary>
internal sealed class ClicheLexicon : IClicheLexicon
{
    private static readonly IReadOnlyDictionary<ResumeLanguage, KnowledgeBankAsset> Assets =
        new Dictionary<ResumeLanguage, KnowledgeBankAsset>
        {
            [ResumeLanguage.Sv] = new("cliche-list", "Jobbliggaren.Infrastructure.KnowledgeBank.cliche-list.sv.v2.json"),
            [ResumeLanguage.En] = new("cliche-list.en", "Jobbliggaren.Infrastructure.KnowledgeBank.cliche-list.en.v1.json"),
        };

    private readonly IReadOnlyDictionary<ResumeLanguage, ClicheList> _lists;

    public ClicheLexicon()
        : this(Assets)
    {
    }

    internal ClicheLexicon(IReadOnlyDictionary<ResumeLanguage, KnowledgeBankAsset> assets) =>
        _lists = KnowledgeBankAsset.LoadForEveryLanguage(assets, "klyschlista", Load);

    public ClicheList GetClicheList(ResumeLanguage language) => _lists[language];

    private static ClicheList Load(KnowledgeBankAsset asset)
    {
        using var stream = asset.Open();

        var file = JsonSerializer.Deserialize<ClicheListFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException($"{asset.ResourceName} deserialiserade till null.");

        var entries = file.Entries
            .Select(e => new ClicheEntry(
                e.Phrase,
                KnowledgeBankTokens.ClicheEntryKind(e.Kind),
                e.Why,
                e.Guidance,
                NullIfBlank(e.DropInReplacement)))
            .ToList();

        return new ClicheList(file.Version, entries, asset.Source);
    }

    // A blank/whitespace dropInReplacement is "no genuine drop-in" — normalise it to null so the
    // propose step's "emit only when a real drop-in exists" gate is a single null check (#495).
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Deserialisation form for the cliché lexicon asset.</summary>
internal sealed record ClicheListFile
{
    [JsonPropertyName("clicheListVersion")]
    public string Version { get; init; } = "unknown";

    [JsonPropertyName("entries")]
    public IReadOnlyList<ClicheEntryFile> Entries { get; init; } = [];

    internal sealed record ClicheEntryFile(
        [property: JsonPropertyName("phrase")] string Phrase,
        [property: JsonPropertyName("why")] string Why,
        [property: JsonPropertyName("guidance")] string Guidance,
        [property: JsonPropertyName("kind")] string? Kind = null,
        [property: JsonPropertyName("dropInReplacement")] string? DropInReplacement = null);
}
