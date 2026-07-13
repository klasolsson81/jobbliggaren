using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The Swedish kommun vocabulary, read from the embedded JobTech taxonomy snapshot (ADR 0043) —
/// the same versioned asset the matching engine already uses. #815: the CV parser needs to
/// recognise a bare city line in a contact block ("Göteborg", with no label and no postal code).
/// A hand-written city list in C# would be a §5 anti-pattern and a second, driftable source of
/// truth; the taxonomy already IS the authority on Swedish place names, so it owns this too.
///
/// Loaded once into a <see cref="FrozenSet{T}"/> (parity with the segmenter's own lexicon and
/// LocalTextAnalyzer's stopwords). Matching is case-insensitive but otherwise exact: this is a
/// recogniser, not a fuzzy geocoder — "Göteborgsvägen" must not resolve to Göteborg.
/// </summary>
internal static class MunicipalityLexicon
{
    private const string SnapshotResourceName =
        "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly FrozenSet<string> Municipalities = Load();

    /// <summary>True when the whole trimmed line is a known Swedish municipality name.</summary>
    internal static bool IsMunicipality(string candidate) =>
        candidate.Length > 0 && Municipalities.Contains(candidate.Trim());

    /// <summary>Exposed for the completeness test that pins the lexicon actually loaded.</summary>
    internal static int Count => Municipalities.Count;

    private static FrozenSet<string> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SnapshotResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded taxonomy snapshot {SnapshotResourceName} was not found.");

        var snapshot = JsonSerializer.Deserialize<Snapshot>(stream, SnapshotJsonOptions)
            ?? throw new InvalidOperationException(
                $"Embedded taxonomy snapshot {SnapshotResourceName} deserialized to null.");

        var names = snapshot.Regions
            .SelectMany(region => region.Municipalities)
            .Select(municipality => municipality.Label.Trim())
            .Where(label => label.Length > 0);

        // Ordinal-ignore-case: a CV may write "GÖTEBORG" in a small-caps rail.
        return names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    // Only the two fields this recogniser needs — the snapshot carries far more (occupation
    // fields, concept ids), and binding to it wholesale would couple parsing to taxonomy shape.
    private sealed record Snapshot(SnapshotRegion[] Regions);

    private sealed record SnapshotRegion(SnapshotMunicipality[] Municipalities);

    private sealed record SnapshotMunicipality(string Label);
}
