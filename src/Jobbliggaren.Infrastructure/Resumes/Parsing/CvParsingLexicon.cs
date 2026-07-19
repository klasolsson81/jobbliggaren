using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The loaded CV-parsing lexicon (F4-8, ADR 0071/0074 — NO AI/LLM): immutable reference data, built
/// once by <see cref="CvParsingLexiconLoader"/> and injected into every consumer. There is exactly
/// ONE instance (a DI singleton registered from an already-loaded value), so the segmenter's
/// RECOGNITION and the port's section-id RESOLUTION are provably reading the same data.
/// </summary>
internal sealed record CvParsingLexiconData(
    int Version,
    FrozenDictionary<string, ParsedSectionKind> HeadingMap,
    FrozenDictionary<string, string> FreeSectionIdByHeading,
    FrozenDictionary<string, string> DisplayFormByHeading,
    FrozenSet<string> FreeSectionIds,
    FrozenSet<string> SwedishHints,
    FrozenSet<string> EnglishHints,
    FrozenSet<string> NameBanners,
    FrozenSet<string> LocationLabels);

/// <summary>
/// Loads the versioned embedded lexicon (CLAUDE.md §5 — vocabulary is data, never inline C# strings).
/// Fails LOUD: a missing resource, a null deserialise, an absent section block or a synonym claimed
/// by two ids all throw here, at <c>AddCvParsing()</c> — i.e. at host build, never mid-request.
///
/// <para><b>Why a loader and not a static class with a static ctor.</b> The first draft of 8b.4a put
/// the frozen structures in <c>static readonly</c> fields. That loads on FIRST USE, which for a
/// segmenter is inside a user's CV import: a broken asset would have surfaced as a
/// <c>TypeInitializationException</c> → HTTP 500, cached for the life of the process, instead of a
/// failed boot. It also made the segmenter untestable against anything but the shipped asset. This
/// is the form <c>RubricProvider</c> already uses, and its reason is the same one.</para>
/// </summary>
internal static partial class CvParsingLexiconLoader
{
    private const string ResourceName =
        "Jobbliggaren.Infrastructure.Resumes.Parsing.cv-parsing-lexicon.v1.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    internal static CvParsingLexiconData Load()
    {
        var assembly = typeof(CvParsingLexiconLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded CV-parsing lexicon missing: {ResourceName}. " +
                "Verify <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");

        return LoadFrom(stream);
    }

    /// <summary>The test seam (parity <c>RubricLoader.LoadFrom</c>): the same build over a synthetic
    /// lexicon, so the loader's fail-loud rules can be exercised without shipping a broken asset.</summary>
    internal static CvParsingLexiconData LoadFrom(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var file = JsonSerializer.Deserialize<LexiconFile>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidOperationException(
                $"CV-parsing lexicon {ResourceName} deserialized to null.");

        if (file.Version <= 0)
            throw new InvalidOperationException("CV-parsing lexicon carries no usable \"version\".");

        // Non-nullable in the record, but System.Text.Json leaves a missing key null — so a typo'd
        // or dropped block would surface as a NullReferenceException deep inside the build below,
        // with no message. Same fail-loud treatment as the rest (minor, dotnet-architect).
        if (file.Headings is null or { Count: 0 })
            throw new InvalidOperationException("CV-parsing lexicon has no \"headings\" block.");

        if (file.LanguageHints is null or { Count: 0 })
            throw new InvalidOperationException("CV-parsing lexicon has no \"languageHints\" block.");

        if (file.FreeSections is null or { Count: 0 })
            throw new InvalidOperationException("CV-parsing lexicon has no \"freeSections\" block.");

        var headingMap = new Dictionary<string, ParsedSectionKind>(StringComparer.Ordinal);
        foreach (var (sectionKey, variants) in file.Headings)
        {
            // An unknown key used to be skipped SILENTLY — so a typo ("experiance") would have made
            // every "Erfarenhet" heading quietly stop being recognised, in the one class whose whole
            // thesis is fail-loud single ownership (minor, dotnet-architect).
            var kind = MapSection(sectionKey);

            foreach (var variant in variants)
                headingMap[NormalizeHeading(variant)] = kind;
        }

        var freeById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (sectionId, synonyms) in file.FreeSections)
        {
            foreach (var synonym in synonyms)
            {
                var normalized = NormalizeHeading(synonym);
                if (normalized.Length == 0)
                    continue;

                // Not a last-one-wins: which section a heading denotes would depend on JSON key
                // order, and the recommendation side would suppress a suggestion for one id while
                // the CV was recognised as the other.
                if (freeById.TryGetValue(normalized, out var claimed) && claimed != sectionId)
                {
                    throw new InvalidOperationException(
                        $"CV-parsing lexicon v{file.Version}: the free-section synonym '{normalized}' " +
                        $"is claimed by BOTH '{claimed}' and '{sectionId}'. One heading cannot denote " +
                        "two sections.");
                }

                freeById[normalized] = sectionId;
            }
        }

        // A free synonym that is ALSO a typed heading would make a typed section (Erfarenhet) resolve
        // as a free one — it changes how every CV is segmented. Pinned by the integrity suite against
        // the SHIPPED data; enforced here so a synthetic or future lexicon cannot smuggle it in.
        var collisions = freeById.Keys.Where(headingMap.ContainsKey).ToList();
        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(
                $"CV-parsing lexicon v{file.Version}: these headings are BOTH typed and free: " +
                $"{string.Join(", ", collisions)}. The collision silently changes segmentation.");
        }

        // v6 (#893): displayForms carry a canonical CASING for a synonym that a pure NormalizeCase
        // cannot recover ("it-kompetenser" → "IT-kompetenser"). The D6 heading-normalization proposal
        // reads this; segmentation never does. THREE fail-loud invariants reconstruct the no-synthesis
        // guarantee D6 gives up by leaving FromStructuralOp (a KB value has no After==pureTransform(Before)
        // check — ADR 0074 / ADR 0108 §5 / CLAUDE.md §5):
        //   INV-1 a display form must key on a KNOWN synonym (typed or free) — a dangling form can never
        //         be proposed, so it is a silent mistake; fail loud, parity the unknown-typed-key throw.
        //   INV-2 a display form must be a pure RE-CASING of the synonym: it may differ from the (already
        //         normalized) key ONLY by letter case — string.Equals(form, key, OrdinalIgnoreCase). So
        //         "IT-kompetenser" passes while "Kompetenser" (a remap), "IT-kompetenser:" (a trailing
        //         separator) and "Tekniska  kompetenser" (collapsed whitespace) all throw: proposing any
        //         of them would rewrite the user's word or add characters they did not write. A weaker
        //         NormalizeHeading(form)==key check would ALSO strip a trailing ':'/'.' and collapse
        //         whitespace, so the promise "re-casing" would drift from what it enforces.
        //   INV-3 one key, one canonical form — parity the freeById "claimed by BOTH" throw above: two
        //         raw keys normalizing to the same key with DIFFERENT forms are an order-dependent silent
        //         last-one-wins, so fail loud (an identical duplicate is idempotent, allowed).
        var displayForms = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawSynonym, form) in file.DisplayForms ?? [])
        {
            var key = NormalizeHeading(rawSynonym);
            if (key.Length == 0 || (!headingMap.ContainsKey(key) && !freeById.ContainsKey(key)))
            {
                throw new InvalidOperationException(
                    $"CV-parsing lexicon v{file.Version}: displayForm key '{rawSynonym}' is not a known " +
                    "heading synonym (neither typed nor free). A dangling display form can never be " +
                    "proposed — fail loud, not silent.");
            }

            if (!string.Equals(form, key, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CV-parsing lexicon v{file.Version}: displayForm '{form}' for synonym '{key}' is not a " +
                    "pure re-casing — it differs by more than letter case (a remap, a trailing separator, or " +
                    "collapsed whitespace). Proposing it would rewrite the user's word or add characters they " +
                    "did not write — synthesis (ADR 0074 / ADR 0108 §5 / CLAUDE.md §5). A display form re-cases " +
                    "the synonym; it changes nothing else.");
            }

            if (displayForms.TryGetValue(key, out var existing)
                && !string.Equals(existing, form, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"CV-parsing lexicon v{file.Version}: the display-form key '{key}' is claimed by BOTH " +
                    $"'{existing}' and '{form}'. One heading cannot have two canonical forms.");
            }

            displayForms[key] = form;
        }

        return new CvParsingLexiconData(
            file.Version,
            headingMap.ToFrozenDictionary(StringComparer.Ordinal),
            freeById.ToFrozenDictionary(StringComparer.Ordinal),
            displayForms.ToFrozenDictionary(StringComparer.Ordinal),
            file.FreeSections.Keys.ToFrozenSet(StringComparer.Ordinal),
            ToHintSet(file.LanguageHints, "sv"),
            ToHintSet(file.LanguageHints, "en"),
            (file.NameBanners ?? []).Select(NormalizeHeading).Where(b => b.Length > 0)
                .ToFrozenSet(StringComparer.Ordinal),
            (file.ContactLabels?.Location ?? []).Select(l => l.Trim().ToLowerInvariant())
                .Where(l => l.Length > 0).ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// THE normalizer — lower-invariant, trim, strip a trailing ':'/'.', collapse internal whitespace.
    ///
    /// <para>Every heading the lexicon STORES and every heading line a CV PRESENTS passes through
    /// this one function. That is not tidiness: the two must agree exactly, or a heading silently
    /// stops matching. Before this it was applied to free headings and name banners but NOT to the
    /// typed ones (they got a bare <c>ToLowerInvariant()</c>), so a typed variant added with a
    /// trailing colon or a double space would have been dead on arrival — in the map that decides
    /// what "Erfarenhet" means. The integrity suite carried a third, weaker copy (no whitespace
    /// collapse) and it was the copy guarding the data.</para>
    /// </summary>
    internal static string NormalizeHeading(string line)
    {
        var trimmed = line.Trim().TrimEnd(':', '.', ' ', '\t');
        if (trimmed.Length == 0)
            return string.Empty;

        return WhitespaceRegex().Replace(trimmed.ToLowerInvariant(), " ");
    }

    private static ParsedSectionKind MapSection(string key) =>
        TryMapTypedSectionId(key, out var kind)
            ? kind
            : throw new InvalidOperationException(
                $"CV-parsing lexicon: unknown typed-heading key '{key}'. A skipped key would make every " +
                "heading under it silently stop being recognised.");

    /// <summary>
    /// The TYPED section id-space, in ONE home. The six typed ids are keys of this lexicon's
    /// <c>headings</c> block and members of <see cref="ParsedSectionKind"/>; both this loader and
    /// <c>CvConventionsLoader</c> (8b.4b) must agree on what "experience" denotes, and a second
    /// switch in the other file would be a fork of exactly the kind ADR 0107 §3 forbids for the
    /// free-section ids. Deliberately NOT <c>Enum.TryParse</c>: that also accepts "0" and "3", so a
    /// numeric typo in an asset would resolve to a real section instead of failing loud.
    /// </summary>
    internal static bool TryMapTypedSectionId(string key, out ParsedSectionKind kind)
    {
        switch (key.ToLowerInvariant())
        {
            case "contact": kind = ParsedSectionKind.Contact; return true;
            case "profile": kind = ParsedSectionKind.Profile; return true;
            case "experience": kind = ParsedSectionKind.Experience; return true;
            case "education": kind = ParsedSectionKind.Education; return true;
            case "skills": kind = ParsedSectionKind.Skills; return true;
            case "languages": kind = ParsedSectionKind.Languages; return true;
            default: kind = default; return false;
        }
    }

    private static FrozenSet<string> ToHintSet(Dictionary<string, string[]> hints, string key) =>
        hints.TryGetValue(key, out var words)
            ? words.Select(w => w.ToLowerInvariant()).ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record LexiconFile(
        int Version,
        Dictionary<string, string[]>? Headings,
        Dictionary<string, string[]>? LanguageHints,
        string[]? NameBanners,
        ContactLabelsFile? ContactLabels,
        Dictionary<string, string[]>? FreeSections,
        Dictionary<string, string>? DisplayForms);

    /// <summary>Contact-field label vocabulary — versioned data, never inline C# strings (§5).</summary>
    private sealed record ContactLabelsFile(string[]? Location);
}
