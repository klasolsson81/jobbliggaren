using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.JobAds.JuniorIt;

/// <summary>
/// The committed, versioned junior IT list (<c>junior-it-list.v1.json</c>, ADR 0186): the curated Platsbanken ids, the
/// headline pattern and the occupation field the pattern applies to.
/// </summary>
internal sealed record JuniorItList(
    string Version,
    DateOnly ReadAt,
    string OccupationFieldConceptId,
    string HeadlinePattern,
    IReadOnlyList<string> CuratedExternalIds);

/// <summary>
/// Loads <see cref="JuniorItList"/> from the assembly. Every shape check fails loud at host build (the list is
/// instance-registered), because a malformed list is a page that silently lists nothing: an empty field id, a pattern
/// that does not compile, or an id that can never equal a Platsbanken id.
/// </summary>
internal static class JuniorItListLoader
{
    private const string ResourceName = "Jobbliggaren.Infrastructure.JobAds.JuniorIt.junior-it-list.v1.json";

    // Platsbanken ids are decimal digits; anything else could never match a row and would shrink the list unseen.
    private static readonly Regex ExternalIdShape = new("^[0-9]{1,20}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    internal static JuniorItList Load()
    {
        using var stream = typeof(JuniorItListLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded junior IT list missing: {ResourceName}. Check <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");
        return LoadFrom(stream);
    }

    /// <summary>Test seam: drives a synthetic asset through the real checks.</summary>
    internal static JuniorItList LoadFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<JuniorItListFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The junior IT list deserialised to null.");

        if (string.IsNullOrWhiteSpace(file.JuniorItListVersion))
            throw new InvalidOperationException("The junior IT list has no juniorItListVersion.");

        if (!DateOnly.TryParseExact(file.ReadAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var readAt))
            throw new InvalidOperationException("The junior IT list's readAt is not a yyyy-MM-dd date.");

        if (string.IsNullOrWhiteSpace(file.OccupationFieldConceptId))
            throw new InvalidOperationException("The junior IT list has no occupationFieldConceptId.");

        if (string.IsNullOrWhiteSpace(file.HeadlinePattern))
            throw new InvalidOperationException("The junior IT list has no headlinePattern.");

        try
        {
            _ = new Regex(file.HeadlinePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException("The junior IT list's headlinePattern does not compile.", e);
        }

        if (file.CuratedExternalIds is null)
            throw new InvalidOperationException("The junior IT list has no curatedExternalIds.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in file.CuratedExternalIds)
        {
            if (id is null || !ExternalIdShape.IsMatch(id))
                throw new InvalidOperationException("The junior IT list has a curated id that is not a Platsbanken id.");
            if (!seen.Add(id))
                throw new InvalidOperationException("The junior IT list names one curated id twice.");
        }

        return new JuniorItList(
            file.JuniorItListVersion,
            readAt,
            file.OccupationFieldConceptId,
            file.HeadlinePattern,
            file.CuratedExternalIds);
    }
}

/// <summary>Deserialisation form; the JSON member names stay in Infrastructure. The <c>"//"</c> key is maintainer
/// documentation and is deliberately not mapped.</summary>
internal sealed record JuniorItListFile
{
    [JsonPropertyName("juniorItListVersion")]
    public string JuniorItListVersion { get; init; } = "";

    [JsonPropertyName("readAt")]
    public string ReadAt { get; init; } = "";

    [JsonPropertyName("occupationFieldConceptId")]
    public string OccupationFieldConceptId { get; init; } = "";

    [JsonPropertyName("headlinePattern")]
    public string HeadlinePattern { get; init; } = "";

    [JsonPropertyName("curatedExternalIds")]
    public IReadOnlyList<string> CuratedExternalIds { get; init; } = [];
}
