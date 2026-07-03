using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IClicheLexicon"/> over the committed, versioned CV lexicon
/// (<c>cliche-list.v2.json</c>, F4-7, research §6.1). Loads + maps the embedded asset
/// once at construction and serves the cached immutable contract — registered as a
/// singleton. v2 (#490/#495/#496): every entry carries a <c>kind</c> discriminator, an
/// advisory <c>guidance</c> field, and an OPTIONAL genuine <c>dropInReplacement</c> (the
/// only literal the propose step may apply verbatim — no synthesis).
/// </summary>
internal sealed class ClicheLexicon : IClicheLexicon
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.cliche-list.v2.json";

    private readonly ClicheList _list = Load();

    public ClicheList GetClicheList() => _list;

    private static ClicheList Load()
    {
        var asm = typeof(ClicheLexicon).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded cliché-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        var file = JsonSerializer.Deserialize<ClicheListFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("cliche-list.v2.json deserialiserade till null.");

        var entries = file.Entries
            .Select(e => new ClicheEntry(
                e.Phrase,
                KnowledgeBankTokens.ClicheEntryKind(e.Kind),
                e.Why,
                e.Guidance,
                NullIfBlank(e.DropInReplacement)))
            .ToList();

        return new ClicheList(file.Version, entries);
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
