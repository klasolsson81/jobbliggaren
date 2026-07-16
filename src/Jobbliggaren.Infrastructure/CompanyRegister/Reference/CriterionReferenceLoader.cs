using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Reference;

/// <summary>
/// #560 PR-3 (CTO Fork G2) — loads the committed, versioned SCB reference datasets
/// (<c>sni-2025.v1.json</c> + <c>scb-kommuner-2026.v1.json</c>) embedded in this assembly and maps
/// them to the immutable Application contracts (<see cref="SniReferenceCatalog"/> /
/// <see cref="KommunReferenceCatalog"/>). No <c>*File</c> type and no <c>JsonPropertyName</c> leaks
/// past Infrastructure (CLAUDE.md §2.1, parity <c>BranschgruppLoader</c>).
///
/// <para>
/// <b>Every shape check here fails LOUD at host build</b> (the provider is instance-registered):
/// a silently dropped or malformed entry is the vacuous-filter failure mode — a picker offering a
/// code the validator rejects, or a validator accepting a code the register can never match. The
/// checks assert form and referential integrity, not counts: SCB revisions legitimately change how
/// many codes exist (tests pin the current counts instead).
/// </para>
/// </summary>
internal static partial class CriterionReferenceLoader
{
    private const string SniResourceName =
        "Jobbliggaren.Infrastructure.CompanyRegister.Reference.sni-2025.v1.json";

    private const string KommunResourceName =
        "Jobbliggaren.Infrastructure.CompanyRegister.Reference.scb-kommuner-2026.v1.json";

    // Mirrors CompanyWatchCriteriaSpec's guards ([0-9], never \d — Unicode digits must not pass;
    // \z, never $ — an embedded newline must not pass). The dataset must satisfy the SAME format
    // the Domain enforces on user input, or "exists in the catalog" and "storable on a criterion"
    // silently diverge.
    [GeneratedRegex(@"^[0-9]{5}\z")]
    private static partial Regex SniLeafPattern();

    [GeneratedRegex(@"^[0-9]{2}\z")]
    private static partial Regex TwoDigitPattern();

    [GeneratedRegex(@"^[0-9]{4}\z")]
    private static partial Regex KommunPattern();

    // A–V, not A–U: SNI 2025 (NACE Rev. 2.1) has 22 sections — "V" is VERKSAMHET VID
    // INTERNATIONELLA ORGANISATIONER, a new top letter relative to SNI 2007. An [A-U] pattern
    // rejected the real asset at host build (caught by the real-asset pin, 2026-07-16).
    [GeneratedRegex(@"^[A-V]\z")]
    private static partial Regex SectionPattern();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    internal static SniReferenceCatalog LoadSni()
    {
        using var stream = OpenResource(SniResourceName);
        return LoadSniFrom(stream);
    }

    internal static KommunReferenceCatalog LoadKommuner()
    {
        using var stream = OpenResource(KommunResourceName);
        return LoadKommunerFrom(stream);
    }

    /// <summary>Test seam — drives synthetic malformed assets through the REAL path (parity
    /// <c>BranschgruppLoader.LoadFrom</c>).</summary>
    internal static SniReferenceCatalog LoadSniFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<SniFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("SNI-referensassetet deserialiserade till null.");

        if (string.IsNullOrWhiteSpace(file.SniVersion))
            throw new InvalidOperationException("SNI-referensassetet saknar sniVersion.");

        if (file.Sections.Count == 0 || file.Divisions.Count == 0 || file.Leaves.Count == 0)
            throw new InvalidOperationException(
                "SNI-referensassetet har en tom nivå (sections/divisions/leaves).");

        var sections = new List<SniSection>(file.Sections.Count);
        var sectionCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in file.Sections)
        {
            if (s.Code is null || !SectionPattern().IsMatch(s.Code))
                throw new InvalidOperationException($"Ogiltig SNI-avdelningskod: '{s.Code}'.");
            if (string.IsNullOrWhiteSpace(s.Name))
                throw new InvalidOperationException($"SNI-avdelning '{s.Code}' saknar namn.");
            if (!sectionCodes.Add(s.Code))
                throw new InvalidOperationException($"SNI-avdelning '{s.Code}' är deklarerad två gånger.");
            sections.Add(new SniSection(s.Code, s.Name.Trim()));
        }

        var divisions = new List<SniDivision>(file.Divisions.Count);
        var divisionCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in file.Divisions)
        {
            if (d.Code is null || !TwoDigitPattern().IsMatch(d.Code))
                throw new InvalidOperationException($"Ogiltig SNI-huvudgruppskod: '{d.Code}'.");
            if (string.IsNullOrWhiteSpace(d.Name))
                throw new InvalidOperationException($"SNI-huvudgrupp '{d.Code}' saknar namn.");
            if (d.Section is null || !sectionCodes.Contains(d.Section))
                throw new InvalidOperationException(
                    $"SNI-huvudgrupp '{d.Code}' pekar på okänd avdelning '{d.Section}'.");
            if (!divisionCodes.Add(d.Code))
                throw new InvalidOperationException($"SNI-huvudgrupp '{d.Code}' är deklarerad två gånger.");
            divisions.Add(new SniDivision(d.Code, d.Section, d.Name.Trim()));
        }

        var leaves = new List<SniLeaf>(file.Leaves.Count);
        var leafCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in file.Leaves)
        {
            if (l.Code is null || !SniLeafPattern().IsMatch(l.Code))
                throw new InvalidOperationException($"Ogiltig SNI-lövkod: '{l.Code}'.");
            if (string.IsNullOrWhiteSpace(l.Name))
                throw new InvalidOperationException($"SNI-löv '{l.Code}' saknar namn.");
            if (l.Division is null || !divisionCodes.Contains(l.Division))
                throw new InvalidOperationException(
                    $"SNI-löv '{l.Code}' pekar på okänd huvudgrupp '{l.Division}'.");
            if (!string.Equals(l.Division, l.Code[..2], StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"SNI-löv '{l.Code}' bär huvudgrupp '{l.Division}' som inte är kodens prefix.");
            if (!leafCodes.Add(l.Code))
                throw new InvalidOperationException($"SNI-löv '{l.Code}' är deklarerat två gånger.");
            leaves.Add(new SniLeaf(l.Code, l.Division, l.Name.Trim()));
        }

        return new SniReferenceCatalog(file.SniVersion, sections, divisions, leaves);
    }

    /// <summary>Test seam — see <see cref="LoadSniFrom"/>.</summary>
    internal static KommunReferenceCatalog LoadKommunerFrom(Stream stream)
    {
        var file = JsonSerializer.Deserialize<KommunFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Kommun-referensassetet deserialiserade till null.");

        if (string.IsNullOrWhiteSpace(file.KommunVersion))
            throw new InvalidOperationException("Kommun-referensassetet saknar kommunVersion.");

        if (file.Lan.Count == 0 || file.Kommuner.Count == 0)
            throw new InvalidOperationException("Kommun-referensassetet har en tom nivå (lan/kommuner).");

        var lan = new List<LanEntry>(file.Lan.Count);
        var lanCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in file.Lan)
        {
            if (l.Code is null || !TwoDigitPattern().IsMatch(l.Code))
                throw new InvalidOperationException($"Ogiltig länskod: '{l.Code}'.");
            if (string.IsNullOrWhiteSpace(l.Name))
                throw new InvalidOperationException($"Län '{l.Code}' saknar namn.");
            if (!lanCodes.Add(l.Code))
                throw new InvalidOperationException($"Län '{l.Code}' är deklarerat två gånger.");
            lan.Add(new LanEntry(l.Code, l.Name.Trim()));
        }

        var kommuner = new List<KommunEntry>(file.Kommuner.Count);
        var kommunCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in file.Kommuner)
        {
            if (k.Code is null || !KommunPattern().IsMatch(k.Code))
                throw new InvalidOperationException($"Ogiltig kommunkod: '{k.Code}'.");
            if (string.IsNullOrWhiteSpace(k.Name))
                throw new InvalidOperationException($"Kommun '{k.Code}' saknar namn.");
            if (k.LanCode is null || !lanCodes.Contains(k.LanCode))
                throw new InvalidOperationException(
                    $"Kommun '{k.Code}' pekar på okänt län '{k.LanCode}'.");
            if (!string.Equals(k.LanCode, k.Code[..2], StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Kommun '{k.Code}' bär län '{k.LanCode}' som inte är kodens prefix.");
            if (!kommunCodes.Add(k.Code))
                throw new InvalidOperationException($"Kommun '{k.Code}' är deklarerad två gånger.");
            kommuner.Add(new KommunEntry(k.Code, k.Name.Trim(), k.LanCode));
        }

        return new KommunReferenceCatalog(file.KommunVersion, lan, kommuner);
    }

    private static Stream OpenResource(string name)
    {
        var asm = typeof(CriterionReferenceLoader).Assembly;
        return asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded referens-asset saknas: {name}. "
                + "Verifiera <EmbeddedResource> i Jobbliggaren.Infrastructure.csproj.");
    }
}

/// <summary>Deserialisation forms. Infrastructure-only — the JSON member names never cross the
/// Application boundary (parity <c>BranschgruppFile</c>). The top-level <c>"//"</c> attribution key
/// is maintainer documentation and is deliberately not mapped.</summary>
internal sealed record SniFile
{
    [JsonPropertyName("sniVersion")]
    public string SniVersion { get; init; } = "";

    [JsonPropertyName("sections")]
    public IReadOnlyList<SectionFile> Sections { get; init; } = [];

    [JsonPropertyName("divisions")]
    public IReadOnlyList<DivisionFile> Divisions { get; init; } = [];

    [JsonPropertyName("leaves")]
    public IReadOnlyList<LeafFile> Leaves { get; init; } = [];

    internal sealed record SectionFile(
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("name")] string? Name = null);

    internal sealed record DivisionFile(
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("section")] string? Section = null,
        [property: JsonPropertyName("name")] string? Name = null);

    internal sealed record LeafFile(
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("division")] string? Division = null,
        [property: JsonPropertyName("name")] string? Name = null);
}

/// <summary>See <see cref="SniFile"/>.</summary>
internal sealed record KommunFile
{
    [JsonPropertyName("kommunVersion")]
    public string KommunVersion { get; init; } = "";

    [JsonPropertyName("lan")]
    public IReadOnlyList<LanFile> Lan { get; init; } = [];

    [JsonPropertyName("kommuner")]
    public IReadOnlyList<KommunEntryFile> Kommuner { get; init; } = [];

    internal sealed record LanFile(
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("name")] string? Name = null);

    internal sealed record KommunEntryFile(
        [property: JsonPropertyName("code")] string? Code = null,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("lanCode")] string? LanCode = null);
}
