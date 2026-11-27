using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister.Reference;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The CV import's place vocabulary (#2343, senior-cto-advisor D7): Sweden's kommuner and its
/// tätorter with a population in 2025, both from SCB (CC0). A place matches as a whole fragment,
/// case-insensitively, after the stored names are normalised (<see cref="PlaceNames"/>); never as
/// a substring, so "Göteborgsvägen" is not Göteborg. Built once at host start and registered as an
/// instance, so a broken asset stops the host instead of a user's import.
/// </summary>
internal sealed class CvPlaceLexicon
{
    private readonly FrozenSet<string> _places;

    internal CvPlaceLexicon(string kommunVersion, string tatortVersion, IEnumerable<string> placeNames)
    {
        KommunVersion = kommunVersion;
        TatortVersion = tatortVersion;
        _places = placeNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    internal string KommunVersion { get; }

    internal string TatortVersion { get; }

    internal int Count => _places.Count;

    /// <summary>The whole trimmed fragment is a place name. <see cref="ContactPatterns"/> asks this for
    /// every call site, after its own normalisation of the fragment.</summary>
    internal bool Contains(string fragment)
    {
        var trimmed = fragment.Trim();
        return trimmed.Length > 0 && _places.Contains(trimmed);
    }
}

/// <summary>The place names one SCB name stands for. A matching step: the stored string is never changed.</summary>
internal static partial class PlaceNames
{
    // SCB's notes, such as "(preliminär beteckning)" and "(2015, 2020: Sundsvall)", are not part of the place.
    [GeneratedRegex(@"\s*\([^()]*\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingNote();

    /// <summary>Trimmed, without a trailing note, split on ", " and " och ", without empties or repeats.</summary>
    internal static IReadOnlyList<string> Keys(string name) =>
        [.. TrailingNote().Replace(name.Trim(), string.Empty)
            .Split([", ", " och "], StringSplitOptions.None)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

internal static partial class CvPlaceLexiconLoader
{
    private const string TatortResourceName =
        "Jobbliggaren.Infrastructure.Resumes.Parsing.scb-tatorter-2025.v1.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // The 4-digit kommun code, "TB" or "TC", and a 3-digit serial.
    [GeneratedRegex(@"^(\d{4})T[BC]\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TatortCode();

    internal static CvPlaceLexicon Load()
    {
        var kommuner = CriterionReferenceLoader.LoadKommuner();
        using var stream = typeof(CvPlaceLexiconLoader).Assembly.GetManifestResourceStream(TatortResourceName)
            ?? throw new InvalidOperationException(
                $"Tätortsresursen '{TatortResourceName}' saknas. Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
        return LoadFrom(kommuner, stream);
    }

    internal static CvPlaceLexicon LoadFrom(KommunReferenceCatalog kommuner, Stream tatorter)
    {
        ArgumentNullException.ThrowIfNull(kommuner);
        ArgumentNullException.ThrowIfNull(tatorter);

        var file = JsonSerializer.Deserialize<TatortFile>(tatorter, Options)
            ?? throw new InvalidOperationException("Tätortsfilen är tom.");
        if (string.IsNullOrWhiteSpace(file.TatortVersion))
            throw new InvalidOperationException("Tätortsfilen saknar tatortVersion.");
        if (file.Tatorter is not { Count: > 0 })
            throw new InvalidOperationException("Tätortsfilen innehåller inga tätorter.");

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var tatort in file.Tatorter)
        {
            var code = tatort.Code ?? string.Empty;
            var match = TatortCode().Match(code);
            if (!match.Success)
                throw new InvalidOperationException($"Tätortskoden '{code}' är felformad.");
            if (!kommuner.Exists(match.Groups[1].Value) || tatort.KommunCode != match.Groups[1].Value)
                throw new InvalidOperationException($"Tätorten {code} pekar på en kommunkod som saknas i SCB:s kommunkatalog.");
            if (!codes.Add(code))
                throw new InvalidOperationException($"Tätortskoden {code} förekommer två gånger.");

            var keys = PlaceNames.Keys(tatort.Name ?? string.Empty);
            if (keys.Count == 0)
                throw new InvalidOperationException($"Tätorten {code} saknar namn.");

            names.AddRange(keys);
        }

        names.AddRange(kommuner.Kommuner.SelectMany(kommun => PlaceNames.Keys(kommun.Name)));
        return new CvPlaceLexicon(kommuner.Version, file.TatortVersion, names);
    }

    private sealed record TatortFile(string? TatortVersion, List<TatortRow>? Tatorter);

    private sealed record TatortRow(string? Code, string? Name, string? KommunCode);
}
