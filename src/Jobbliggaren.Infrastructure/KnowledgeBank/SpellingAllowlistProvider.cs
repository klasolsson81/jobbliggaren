using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="ISpellingAllowlist"/> over the committed, versioned spelling allowlist
/// (<c>spelling-allowlist.v1.json</c>, Fas 4b PR-6, ADR 0093 §D4). Loads + validates the
/// embedded asset once at construction and serves the cached immutable contract —
/// registered as a singleton (parity <see cref="RubricProvider"/>/<see cref="ClicheLexicon"/>).
/// A malformed asset fails the host at startup, never mid-request. The allowlist is the
/// ONLY sanctioned way to suppress a Hunspell false positive: the sv_SE DSSO dictionary is
/// SHA-256-pinned + shipped unmodified (LGPL-3.0, BUILD §3.1), so adding a word to the
/// dictionary is forbidden (CLAUDE.md §5 — versioned KB data, never an inline list).
/// </summary>
internal sealed class SpellingAllowlistProvider : ISpellingAllowlist
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.spelling-allowlist.v1.json";

    private readonly SpellingAllowlist _allowlist = Load();

    public SpellingAllowlist GetAllowlist() => _allowlist;

    private static SpellingAllowlist Load()
    {
        var asm = typeof(SpellingAllowlistProvider).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded spelling-allowlist-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        return LoadFrom(stream);
    }

    /// <summary>Deserialises + maps an allowlist document from an arbitrary stream — the seam
    /// the loader unit tests drive synthetic fixtures through (the REAL deserialise + validate
    /// path, parity <c>RubricLoader.LoadFrom</c>).</summary>
    internal static SpellingAllowlist LoadFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<SpellingAllowlistFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("spelling-allowlist.v1.json deserialiserade till null.");

        if (string.IsNullOrWhiteSpace(file.Version))
        {
            throw new InvalidOperationException(
                "spelling-allowlist-asset saknar allowlistVersion (fail-loud).");
        }

        // A shipped empty allowlist is almost certainly a corrupt/missing asset, not an
        // intentional "suppress nothing" — fail loud rather than silently spell-check with
        // zero proper-noun tolerance (parity RubricLoader's fail-loud posture).
        if (file.Terms.Count == 0)
        {
            throw new InvalidOperationException(
                "spelling-allowlist-asset innehåller inga termer (fail-loud).");
        }

        return new SpellingAllowlist(file.Version, file.Terms);
    }
}

/// <summary>Deserialisation form for the spelling-allowlist asset (unknown members — e.g.
/// the leading <c>_comment</c> — are ignored by <see cref="KnowledgeBankJson.Options"/>).</summary>
internal sealed record SpellingAllowlistFile
{
    [JsonPropertyName("allowlistVersion")]
    public string Version { get; init; } = "unknown";

    [JsonPropertyName("terms")]
    public IReadOnlyList<string> Terms { get; init; } = [];
}
