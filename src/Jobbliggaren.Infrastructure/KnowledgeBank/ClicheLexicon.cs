using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IClicheLexicon"/> over the committed, versioned cliché lexicon
/// (<c>cliche-list.v1.json</c>, F4-7, research §6.1). Loads + maps the embedded asset
/// once at construction and serves the cached immutable contract — registered as a
/// singleton.
/// </summary>
internal sealed class ClicheLexicon : IClicheLexicon
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.cliche-list.v1.json";

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
            ?? throw new InvalidOperationException("cliche-list.v1.json deserialiserade till null.");

        var entries = file.Entries
            .Select(e => new ClicheEntry(e.Phrase, e.Why, e.BetterAlternative))
            .ToList();

        return new ClicheList(file.Version, entries);
    }
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
        [property: JsonPropertyName("betterAlternative")] string BetterAlternative);
}
