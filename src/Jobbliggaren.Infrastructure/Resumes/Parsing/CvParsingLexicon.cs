using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The ONE owner of the embedded CV-parsing lexicon (F4-8, ADR 0071/0074 — NO AI/LLM). Loads the
/// versioned JSON once into immutable frozen structures; every consumer reads THESE, never the file.
///
/// <para><b>Why this type exists (8b.4a).</b> The lexicon owns <b>RECOGNITION</b>: which strings
/// denote which section. A downstream asset owns <b>RECOMMENDATION</b>: which sections to suggest
/// for an occupation. Recommendation must be able to ask "does this CV already have the section I am
/// about to suggest?" — and before v4 it could not, because free sections were a flat token set with
/// no identity (<c>certifieringar</c>, <c>certifikat</c> and <c>kurser</c> were three unrelated
/// strings). The only way to answer without FORKING the synonyms into a second file was to give the
/// lexicon a canonical <c>sectionId</c> and hand it out through a port. That is v4, and this is its
/// single load site — two loads would be two owners of one knowledge piece.</para>
///
/// <para>Segmentation behaviour is UNCHANGED by v4: the flattened synonym union is byte-identical to
/// v3 (pinned by <c>CvParsingLexiconIntegrityTests</c>), and the segmenter still asks the same
/// membership question — it simply gets an id back instead of a bool, and discards it
/// (<c>ParsedSection.Heading</c> stays the user's own line, verbatim).</para>
/// </summary>
internal static partial class CvParsingLexicon
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.Resumes.Parsing.cv-parsing-lexicon.v1.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The lexicon's data version. Before v4 the in-file <c>"version"</c> was read by NOTHING — a
    /// version nobody binds cannot fail loud on drift. It is bound here so a consumer asset can pin
    /// the lexicon version it was authored against (the <c>FramesLoader</c> ↔ <c>IVerbMapper</c>
    /// precedent) and a reshape cannot pass silently.
    /// </summary>
    internal static int Version { get; }

    /// <summary>Normalised heading → the typed section it denotes.</summary>
    internal static FrozenDictionary<string, Domain.Resumes.Parsing.ParsedSectionKind> HeadingMap { get; }

    /// <summary>
    /// Normalised heading → canonical free-section id (v4). The id is the identity a recommendation
    /// asset keys on; the segmenter uses only the fact that a lookup SUCCEEDS.
    /// </summary>
    internal static FrozenDictionary<string, string> FreeSectionIdByHeading { get; }

    /// <summary>Every canonical free-section id, in lexicon order (deterministic).</summary>
    internal static FrozenSet<string> FreeSectionIds { get; }

    internal static FrozenSet<string> SwedishHints { get; }

    internal static FrozenSet<string> EnglishHints { get; }

    /// <summary>#428 — CV-title banners ("Curriculum Vitae", "Meritförteckning") that must NOT be
    /// read as the person's name.</summary>
    internal static FrozenSet<string> NameBanners { get; }

    /// <summary>#815 — the labels that introduce a city ("Ort:", "Bostadsort:", "Location:").</summary>
    internal static FrozenSet<string> LocationLabels { get; }

    static CvParsingLexicon()
    {
        var file = Load();

        Version = file.Version;

        var headingMap = new Dictionary<string, Domain.Resumes.Parsing.ParsedSectionKind>(StringComparer.Ordinal);
        foreach (var (sectionKey, variants) in file.Headings)
        {
            if (!TryMapSection(sectionKey, out var kind))
                continue;

            foreach (var variant in variants)
                headingMap[variant.ToLowerInvariant()] = kind;
        }

        HeadingMap = headingMap.ToFrozenDictionary(StringComparer.Ordinal);

        // v4: sectionId -> synonyms, inverted to synonym -> sectionId (the lookup direction every
        // consumer needs). A synonym claimed by two ids is a lexicon bug, not a last-one-wins:
        // it would make "which section is this?" depend on JSON key order. Fail loud.
        var freeById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (sectionId, synonyms) in file.FreeSections ?? [])
        {
            foreach (var synonym in synonyms)
            {
                var normalized = NormalizeHeading(synonym);
                if (normalized.Length == 0)
                    continue;

                if (freeById.TryGetValue(normalized, out var claimed) && claimed != sectionId)
                {
                    throw new InvalidOperationException(
                        $"CV-parsing lexicon v{file.Version}: the free-section synonym '{normalized}' is " +
                        $"claimed by BOTH '{claimed}' and '{sectionId}'. One heading cannot denote two " +
                        "sections — the resolved id would depend on JSON key order.");
                }

                freeById[normalized] = sectionId;
            }
        }

        FreeSectionIdByHeading = freeById.ToFrozenDictionary(StringComparer.Ordinal);
        FreeSectionIds = (file.FreeSections ?? []).Keys.ToFrozenSet(StringComparer.Ordinal);

        SwedishHints = ToHintSet(file.LanguageHints, "sv");
        EnglishHints = ToHintSet(file.LanguageHints, "en");

        NameBanners = (file.NameBanners ?? [])
            .Select(NormalizeHeading)
            .Where(banner => banner.Length > 0)
            .ToFrozenSet(StringComparer.Ordinal);

        LocationLabels = (file.ContactLabels?.Location ?? [])
            .Select(label => label.Trim().ToLowerInvariant())
            .Where(label => label.Length > 0)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Lower-invariant, trim, strip a trailing ':'/'.', collapse internal whitespace. THE single
    /// normalizer: the lexicon's entries and the CV's heading lines must be normalised by the SAME
    /// function or a heading silently stops matching. (Before 8b.4a this function existed twice.)
    /// </summary>
    internal static string NormalizeHeading(string line)
    {
        var trimmed = line.Trim().TrimEnd(':', '.', ' ', '\t');
        if (trimmed.Length == 0)
            return string.Empty;

        var lowered = trimmed.ToLowerInvariant();
        return WhitespaceRegex().Replace(lowered, " ");
    }

    private static bool TryMapSection(string key, out Domain.Resumes.Parsing.ParsedSectionKind kind)
    {
        switch (key.ToLowerInvariant())
        {
            case "contact": kind = Domain.Resumes.Parsing.ParsedSectionKind.Contact; return true;
            case "profile": kind = Domain.Resumes.Parsing.ParsedSectionKind.Profile; return true;
            case "experience": kind = Domain.Resumes.Parsing.ParsedSectionKind.Experience; return true;
            case "education": kind = Domain.Resumes.Parsing.ParsedSectionKind.Education; return true;
            case "skills": kind = Domain.Resumes.Parsing.ParsedSectionKind.Skills; return true;
            case "languages": kind = Domain.Resumes.Parsing.ParsedSectionKind.Languages; return true;
            default: kind = default; return false;
        }
    }

    private static FrozenSet<string> ToHintSet(Dictionary<string, string[]> hints, string key) =>
        hints.TryGetValue(key, out var words)
            ? words.Select(w => w.ToLowerInvariant()).ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;

    private static LexiconFile Load()
    {
        var assembly = typeof(CvParsingLexicon).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded CV-parsing lexicon missing: {ResourceName}. " +
                "Verify <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var file = JsonSerializer.Deserialize<LexiconFile>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidOperationException(
                $"Embedded CV-parsing lexicon {ResourceName} deserialized to null.");

        if (file.Version <= 0)
        {
            throw new InvalidOperationException(
                $"Embedded CV-parsing lexicon {ResourceName} carries no usable \"version\". " +
                "A consumer asset pins this value to fail loud on drift; an absent version " +
                "would make that pin vacuous.");
        }

        return file;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record LexiconFile(
        int Version,
        Dictionary<string, string[]> Headings,
        Dictionary<string, string[]> LanguageHints,
        string[]? NameBanners,
        ContactLabelsFile? ContactLabels,
        Dictionary<string, string[]>? FreeSections);

    /// <summary>Contact-field label vocabulary — versioned data, never inline C# strings (§5).</summary>
    private sealed record ContactLabelsFile(string[]? Location);
}
