using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// Loads the committed, versioned CV-quality rubric (<c>rubric.v1.1.0.json</c>)
/// embedded in this assembly (F4-7, BUILD §8.1/§8.6, research §2). Deserialises the
/// Swedish-token <see cref="RubricFile"/> DTO and maps it to the English-enum
/// <see cref="Rubric"/> contract — no <c>*File</c> type, no Swedish token, no
/// <c>JsonPropertyName</c> leaks past Infrastructure (CLAUDE.md §2.1, parity with
/// <c>TaxonomySnapshotSeeder.LoadSnapshot</c>).
/// <para>
/// Tolerant deserialisation IS the N-1 mechanism (senior-cto-advisor DQ6): default
/// System.Text.Json ignores unknown members, a missing <c>assessability</c> maps to
/// <see cref="CriterionAssessability.NotAssessedV1"/>, and missing <c>bands</c>/
/// <c>criticalFailIds</c> fall to empty lists — so an older (N-1) rubric still loads.
/// </para>
/// </summary>
internal static class RubricLoader
{
    // The SAME LogicalName the csproj declares for the embedded resource.
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.KnowledgeBank.rubric.v1.1.0.json";

    /// <summary>Loads the committed v1 rubric from the embedded resource.</summary>
    internal static Rubric Load()
    {
        var asm = typeof(RubricLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded rubric-asset saknas: {ResourceName}. " +
                "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");

        return LoadFrom(stream);
    }

    /// <summary>
    /// Deserialises + maps a rubric document from an arbitrary stream. The seam the
    /// N-1 back-compat test drives a synthetic older fixture through (the REAL
    /// deserialise + map + validate path), mirroring how
    /// <c>TaxonomySnapshotSeeder.LoadSnapshot</c> is internal-callable from tests.
    /// </summary>
    internal static Rubric LoadFrom(Stream stream) => MapToContract(Deserialize(stream));

    internal static RubricFile Deserialize(Stream stream) =>
        JsonSerializer.Deserialize<RubricFile>(stream, KnowledgeBankJson.Options)
            ?? throw new InvalidOperationException("rubric-asset deserialiserade till null.");

    internal static Rubric MapToContract(RubricFile file)
    {
        var criteria = file.Criteria
            .Select(MapCriterion)
            .ToList();

        ValidateCriticalFailIds(file.CriticalFailIds, criteria);

        var weights = file.Weights.ToDictionary(
            kvp => KnowledgeBankTokens.Weight(kvp.Key),
            kvp => kvp.Value);

        var categoryWeights = new CategoryWeights(
            MapCategoryWeights(file.CategoryWeights.Ats),
            MapCategoryWeights(file.CategoryWeights.Visual));

        var bands = file.Bands
            .Select(b => new ScoreBand(b.MinInclusive, KnowledgeBankTokens.Band(b.Label)))
            .ToList();

        return new Rubric(
            RubricVersion.Parse(file.RubricVersion),
            ParseEffectiveDate(file.EffectiveDate),
            weights,
            categoryWeights,
            bands,
            file.CriticalFailIds,
            criteria);
    }

    private static RubricCriterion MapCriterion(RubricFile.CriterionFile c)
    {
        var category = KnowledgeBankTokens.Category(c.Category);
        var profile = KnowledgeBankTokens.Profile(c.Profile);

        ValidateCategoryMatchesIdPrefix(c.Id, category);
        ValidateProfileSignalNullability(c.Id, profile,
            c.AtsPassSignal, c.AtsFailSignal, c.VisualPassSignal, c.VisualFailSignal);

        return new RubricCriterion(
            c.Id,
            category,
            c.Name,
            KnowledgeBankTokens.Weight(c.Weight),
            profile,
            KnowledgeBankTokens.Assessability(c.Assessability),
            c.AtsPassSignal,
            c.AtsFailSignal,
            c.VisualPassSignal,
            c.VisualFailSignal,
            c.NotAssessedReason);
    }

    private static Dictionary<RubricCategory, double> MapCategoryWeights(
        Dictionary<string, double> raw) =>
        raw.ToDictionary(
            kvp => KnowledgeBankTokens.CategoryLetter(kvp.Key),
            kvp => kvp.Value);

    private static DateOnly ParseEffectiveDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            // Fail loud on a malformed/missing date — disciplinary parity with the rest
            // of the loader (RubricVersion.Parse + the fail-loud token maps); a silent
            // 0001-01-01 would mask a corrupt asset (code-reviewer Minor 1).
            : throw new InvalidOperationException(
                $"Ogiltigt effectiveDate i rubric-asset: '{value}'. Förväntat ISO-datum (yyyy-MM-dd).");

    private static void ValidateCategoryMatchesIdPrefix(string id, RubricCategory category)
    {
        if (id.Length == 0 || KnowledgeBankTokens.CategoryLetter(id[..1]) != category)
        {
            throw new InvalidOperationException(
                $"Kriterium '{id}': kategori {category} stämmer inte med id-prefixet.");
        }
    }

    private static void ValidateProfileSignalNullability(
        string id, RubricProfile profile,
        string? atsPass, string? atsFail, string? visualPass, string? visualFail)
    {
        var atsPresent = atsPass is not null && atsFail is not null;
        var atsAbsent = atsPass is null && atsFail is null;
        var visualPresent = visualPass is not null && visualFail is not null;
        var visualAbsent = visualPass is null && visualFail is null;

        var ok = profile switch
        {
            RubricProfile.Both => atsPresent && visualPresent,
            RubricProfile.AtsOnly => atsPresent && visualAbsent,
            RubricProfile.VisualOnly => visualPresent && atsAbsent,
            _ => false,
        };

        if (!ok)
        {
            throw new InvalidOperationException(
                $"Kriterium '{id}' ({profile}): signal-nullbarhet bryter profil-regeln " +
                "(Båda = alla fyra; EndastAts = ats satta + visual null; " +
                "EndastVisuell = visual satta + ats null).");
        }
    }

    private static void ValidateCriticalFailIds(
        IReadOnlyList<string> criticalFailIds, IReadOnlyList<RubricCriterion> criteria)
    {
        var ids = criteria.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var dangling = criticalFailIds.Where(id => !ids.Contains(id)).ToList();
        if (dangling.Count > 0)
        {
            throw new InvalidOperationException(
                "criticalFailIds pekar på obefintliga kriterier: " +
                string.Join(", ", dangling) + ".");
        }
    }
}

/// <summary>Deserialisation form for the rubric asset. Swedish tokens kept as raw
/// strings (mapped to English enums in <see cref="RubricLoader.MapToContract"/>);
/// nullable members with defaults are the N-1 / missing-field tolerance.</summary>
internal sealed record RubricFile
{
    [JsonPropertyName("rubricVersion")]
    public string RubricVersion { get; init; } = "unknown";

    [JsonPropertyName("effectiveDate")]
    public string EffectiveDate { get; init; } = "";

    [JsonPropertyName("weights")]
    public Dictionary<string, double> Weights { get; init; } = new();

    [JsonPropertyName("categoryWeights")]
    public CategoryWeightsFile CategoryWeights { get; init; } = new();

    [JsonPropertyName("bands")]
    public IReadOnlyList<BandFile> Bands { get; init; } = [];

    [JsonPropertyName("criticalFailIds")]
    public IReadOnlyList<string> CriticalFailIds { get; init; } = [];

    [JsonPropertyName("criteria")]
    public IReadOnlyList<CriterionFile> Criteria { get; init; } = [];

    internal sealed record CategoryWeightsFile
    {
        [JsonPropertyName("ats")]
        public Dictionary<string, double> Ats { get; init; } = new();

        [JsonPropertyName("visual")]
        public Dictionary<string, double> Visual { get; init; } = new();
    }

    internal sealed record BandFile(
        [property: JsonPropertyName("minInclusive")] int MinInclusive,
        [property: JsonPropertyName("label")] string Label);

    internal sealed record CriterionFile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("weight")] string Weight,
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("assessability")] string? Assessability = null,
        [property: JsonPropertyName("atsPassSignal")] string? AtsPassSignal = null,
        [property: JsonPropertyName("atsFailSignal")] string? AtsFailSignal = null,
        [property: JsonPropertyName("visualPassSignal")] string? VisualPassSignal = null,
        [property: JsonPropertyName("visualFailSignal")] string? VisualFailSignal = null,
        // Nullable + defaulted = N-1 tolerance: an older asset without the field maps to a
        // null reason and the engine falls back to a generic civic default (never throws).
        [property: JsonPropertyName("notAssessedReason")] string? NotAssessedReason = null);
}
