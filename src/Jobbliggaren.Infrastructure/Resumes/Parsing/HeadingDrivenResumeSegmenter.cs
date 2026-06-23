using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministic, heading-driven CV segmentation behind <see cref="IResumeSegmenter"/>
/// (F4-8, NO AI/LLM). Pure string algorithm: detect Swedish/English section headings
/// (from the versioned embedded lexicon, never inline strings — CLAUDE.md §5), split
/// the text into sections, best-effort-parse each, detect the document language, and
/// derive an explainable per-section + document confidence (OQ5). Nothing is
/// synthesised — every field is what was found or honestly absent.
/// </summary>
internal sealed partial class HeadingDrivenResumeSegmenter : IResumeSegmenter
{
    private const string LexiconResourceName =
        "Jobbliggaren.Infrastructure.Resumes.Parsing.cv-parsing-lexicon.v1.json";

    private const int MaxSkills = 200;
    private const int MaxLanguages = 50;
    private const int MaxEntries = 100;

    // Reference data: immutable, loaded once (parity LocalTextAnalyzer.LoadStopwords).
    private static readonly FrozenDictionary<string, ParsedSectionKind> HeadingMap;
    private static readonly FrozenSet<string> SwedishHints;
    private static readonly FrozenSet<string> EnglishHints;

    private static readonly JsonSerializerOptions LexiconJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    static HeadingDrivenResumeSegmenter()
    {
        var lexicon = LoadLexicon();

        var headingMap = new Dictionary<string, ParsedSectionKind>(StringComparer.Ordinal);
        foreach (var (sectionKey, variants) in lexicon.Headings)
        {
            if (!TryMapSection(sectionKey, out var kind))
                continue;

            foreach (var variant in variants)
                headingMap[variant.ToLowerInvariant()] = kind;
        }

        HeadingMap = headingMap.ToFrozenDictionary(StringComparer.Ordinal);
        SwedishHints = ToHintSet(lexicon.LanguageHints, "sv");
        EnglishHints = ToHintSet(lexicon.LanguageHints, "en");
    }

    public ResumeSegmentationResult Segment(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        var lines = SplitLines(rawText);
        var headings = DetectHeadings(lines);
        var blocks = BuildSectionBlocks(lines, headings);
        var preamble = PreambleLines(lines, headings);
        var language = DetectLanguage(rawText);

        var email = FirstEmail(rawText);
        var phone = FirstPhone(rawText);
        var fullName = DetectName(preamble, blocks);
        var contact = new ParsedContact(fullName, email, phone, Location: null);

        var profileText = SectionText(blocks, ParsedSectionKind.Profile);
        var experiences = ParseExperiences(blocks);
        var educations = ParseEducations(blocks);
        var skills = ParseList(blocks, ParsedSectionKind.Skills, MaxSkills);
        var languages = ParseList(blocks, ParsedSectionKind.Languages, MaxLanguages);

        var content = new ParsedResumeContent(
            contact, profileText, experiences, educations, skills, languages);

        var sections = new List<SectionConfidence>
        {
            ContactConfidence(contact),
            ProfileConfidence(headings, profileText),
            ListSectionConfidence(ParsedSectionKind.Experience, headings, experiences.Count),
            ListSectionConfidence(ParsedSectionKind.Education, headings, educations.Count),
            ListSectionConfidence(ParsedSectionKind.Skills, headings, skills.Count),
            ListSectionConfidence(ParsedSectionKind.Languages, headings, languages.Count),
        };

        var confidence = ParseConfidence.FromSections(sections);
        return new ResumeSegmentationResult(content, language, confidence);
    }

    // ── Heading detection ───────────────────────────────────────────────

    private static List<(int Line, ParsedSectionKind Kind, string Matched)>
        DetectHeadings(string[] lines)
    {
        var headings = new List<(int Line, ParsedSectionKind Kind, string Matched)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var normalized = NormalizeHeading(lines[i]);
            if (normalized.Length > 0 && HeadingMap.TryGetValue(normalized, out var kind))
                headings.Add((i, kind, normalized));
        }

        return headings;
    }

    // Lower-invariant, trim, strip trailing ':'/'.', collapse internal whitespace.
    private static string NormalizeHeading(string line)
    {
        var trimmed = line.Trim().TrimEnd(':', '.', ' ', '\t');
        if (trimmed.Length == 0)
            return string.Empty;

        var lowered = trimmed.ToLowerInvariant();
        return WhitespaceRegex().Replace(lowered, " ");
    }

    private static Dictionary<ParsedSectionKind, string> BuildSectionBlocks(
        string[] lines,
        List<(int Line, ParsedSectionKind Kind, string Matched)> headings)
    {
        var blocks = new Dictionary<ParsedSectionKind, string>();
        for (var h = 0; h < headings.Count; h++)
        {
            var start = headings[h].Line + 1;
            var end = h + 1 < headings.Count ? headings[h + 1].Line : lines.Length;
            if (start >= end)
            {
                blocks.TryAdd(headings[h].Kind, string.Empty);
                continue;
            }

            var block = string.Join('\n', lines.Skip(start).Take(end - start)).Trim();
            // Same section heading twice ⇒ concatenate the blocks deterministically.
            blocks[headings[h].Kind] = blocks.TryGetValue(headings[h].Kind, out var existing)
                ? string.Concat(existing, "\n", block).Trim()
                : block;
        }

        return blocks;
    }

    private static List<string> PreambleLines(
        string[] lines,
        List<(int Line, ParsedSectionKind Kind, string Matched)> headings)
    {
        var firstHeading = headings.Count > 0 ? headings[0].Line : lines.Length;
        return lines.Take(firstHeading).ToList();
    }

    // ── Field extraction ────────────────────────────────────────────────

    private static string? SectionText(
        Dictionary<ParsedSectionKind, string> blocks, ParsedSectionKind kind) =>
        blocks.TryGetValue(kind, out var text) && text.Length > 0 ? text : null;

    private static string? DetectName(
        IReadOnlyList<string> preamble, Dictionary<ParsedSectionKind, string> blocks)
    {
        // The name is conventionally the first substantial line at the top of a CV
        // (or under an explicit Kontakt heading) — not an e-mail/phone/heading line.
        foreach (var line in preamble)
        {
            if (IsNameLike(line))
                return line.Trim();
        }

        if (blocks.TryGetValue(ParsedSectionKind.Contact, out var contactBlock))
        {
            foreach (var line in contactBlock.Split('\n'))
            {
                if (IsNameLike(line))
                    return line.Trim();
            }
        }

        return null;
    }

    private static bool IsNameLike(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length is 0 or > 60)
            return false;

        if (EmailRegex().IsMatch(trimmed) || LooksLikePhone(trimmed))
            return false;

        // Must contain at least one letter (avoid pure dates/separators).
        foreach (var c in trimmed)
        {
            if (char.IsLetter(c))
                return true;
        }

        return false;
    }

    private static string? FirstEmail(string text)
    {
        var match = EmailRegex().Match(text);
        return match.Success ? match.Value : null;
    }

    private static string? FirstPhone(string text)
    {
        foreach (Match candidate in PhoneRegex().Matches(text))
        {
            if (CountDigits(candidate.Value) >= 7)
                return candidate.Value.Trim();
        }

        return null;
    }

    private static bool LooksLikePhone(string line)
    {
        var match = PhoneRegex().Match(line);
        return match.Success && CountDigits(match.Value) >= 7;
    }

    private static List<ParsedExperience> ParseExperiences(
        Dictionary<ParsedSectionKind, string> blocks)
    {
        var result = new List<ParsedExperience>();
        if (!blocks.TryGetValue(ParsedSectionKind.Experience, out var block) || block.Length == 0)
            return result;

        foreach (var entry in SplitEntries(block))
        {
            if (result.Count >= MaxEntries)
                break;

            var (title, organization) = SplitTitleOrganization(entry);
            result.Add(new ParsedExperience(title, organization, ExtractPeriod(entry.Text), entry.Text));
        }

        return result;
    }

    private static List<ParsedEducation> ParseEducations(
        Dictionary<ParsedSectionKind, string> blocks)
    {
        var result = new List<ParsedEducation>();
        if (!blocks.TryGetValue(ParsedSectionKind.Education, out var block) || block.Length == 0)
            return result;

        foreach (var entry in SplitEntries(block))
        {
            if (result.Count >= MaxEntries)
                break;

            var (degree, institution) = SplitTitleOrganization(entry);
            result.Add(new ParsedEducation(institution, degree, ExtractPeriod(entry.Text), entry.Text));
        }

        return result;
    }

    private static List<string> ParseList(
        Dictionary<ParsedSectionKind, string> blocks, ParsedSectionKind kind, int cap)
    {
        var result = new List<string>();
        if (!blocks.TryGetValue(kind, out var block) || block.Length == 0)
            return result;

        foreach (var token in ListSeparatorRegex().Split(block))
        {
            var trimmed = token.Trim().TrimStart('•', '-', '*', '·', '–', '—', '|').Trim();
            if (trimmed.Length > 0)
            {
                result.Add(trimmed);
                if (result.Count >= cap)
                    break;
            }
        }

        return result;
    }

    private readonly record struct Entry(IReadOnlyList<string> Lines, string Text);

    // Split a section block into entries on blank lines.
    private static IEnumerable<Entry> SplitEntries(string block)
    {
        var current = new List<string>();
        foreach (var line in block.Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                if (current.Count > 0)
                {
                    yield return new Entry(current, string.Join('\n', current).Trim());
                    current = [];
                }
            }
            else
            {
                current.Add(line.Trim());
            }
        }

        if (current.Count > 0)
            yield return new Entry(current, string.Join('\n', current).Trim());
    }

    // First line → (title, organization) best-effort; falls back to the second line as
    // the organization for the common "Title / Company / Dates" layout.
    private static (string? Title, string? Organization) SplitTitleOrganization(Entry entry)
    {
        if (entry.Lines.Count == 0)
            return (null, null);

        // Strip a TRAILING period from the header line BEFORE the title/organization split
        // so a header that packs the dates on the same line as the role/company
        // ("Plasman — Operatör 2005 – nu") cannot bleed the date into the field after the
        // first separator (the reported layout-split bug). The period itself is still
        // recovered by ExtractPeriod from the full entry text. No-op for the common
        // "Role — Company\nYYYY-YYYY" layout (period on its own line) → no regression.
        var first = StripTrailingPeriod(entry.Lines[0]);
        foreach (var separator in TitleOrgSeparators)
        {
            var index = first.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && index + separator.Length < first.Length)
            {
                var title = first[..index].Trim();
                var organization = first[(index + separator.Length)..].Trim();
                return (NullIfEmpty(title), NullIfEmpty(organization));
            }
        }

        var org = entry.Lines.Count >= 2 ? NullIfEmpty(entry.Lines[1].Trim()) : null;
        return (NullIfEmpty(first.Trim()), org);
    }

    // Remove a TRAILING date range or year from a header line, reusing the same patterns
    // ExtractPeriod uses. Only strips when the date/year is at the END (a leading or internal
    // year is likely part of the name, e.g. "Studio 2005 Design", and is left alone). The slot
    // ORDER (which side is the role vs the company) is deliberately NOT guessed — a layout-naive
    // CV may put either in either slot, so the user corrects it via the editable gap-fill
    // (ADR 0040 propose-and-approve; senior-cto-advisor bind 2026-06-23).
    private static string StripTrailingPeriod(string line)
    {
        var range = DateRangeRegex().Match(line);
        if (range.Success && line[(range.Index + range.Length)..].Trim().Length == 0)
            return TrimTrailingSeparators(line[..range.Index]);

        var year = YearRegex().Match(line);
        if (year.Success && line[(year.Index + year.Length)..].Trim().Length == 0)
            return TrimTrailingSeparators(line[..year.Index]);

        return line;
    }

    private static string TrimTrailingSeparators(string value) =>
        value.TrimEnd(' ', '\t', ',', ';', '|', '-', '–', '—');

    private static readonly string[] TitleOrgSeparators =
        [" — ", " – ", " - ", ", ", " | ", " @ ", " at ", " på ", " hos "];

    private static string? ExtractPeriod(string text)
    {
        var range = DateRangeRegex().Match(text);
        if (range.Success)
            return range.Value.Trim();

        var year = YearRegex().Match(text);
        return year.Success ? year.Value : null;
    }

    // ── Language detection (F4-8 scope; English analysis deferred to F4-9) ──

    private static ResumeLanguage DetectLanguage(string text)
    {
        var swedish = 0;
        var english = 0;
        foreach (var word in Tokenize(text))
        {
            if (SwedishHints.Contains(word))
                swedish++;
            if (EnglishHints.Contains(word))
                english++;
        }

        // Default to Swedish on a tie or no signal (the Swedish-market baseline).
        return english > swedish ? ResumeLanguage.En : ResumeLanguage.Sv;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetter(text[i]))
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                yield return text[start..i].ToLowerInvariant();
                start = -1;
            }
        }

        if (start >= 0)
            yield return text[start..].ToLowerInvariant();
    }

    // ── Confidence (explainable, structural evidence only — never PII) ──

    private static SectionConfidence ContactConfidence(ParsedContact contact)
    {
        var hasName = contact.FullName is { Length: > 0 };
        var hasEmail = contact.Email is { Length: > 0 };
        var hasPhone = contact.Phone is { Length: > 0 };

        var evidence = new List<string>();
        if (hasName) evidence.Add("name extracted");
        if (hasEmail) evidence.Add("email extracted");
        if (hasPhone) evidence.Add("phone extracted");

        SectionConfidenceLevel level;
        if (hasName && (hasEmail || hasPhone))
            level = SectionConfidenceLevel.Confident;
        else if (hasName || hasEmail || hasPhone)
            level = SectionConfidenceLevel.Degraded;
        else
        {
            level = SectionConfidenceLevel.NotFound;
            evidence.Add("no contact fields detected");
        }

        return new SectionConfidence(ParsedSectionKind.Contact, level, evidence);
    }

    private static SectionConfidence ProfileConfidence(
        List<(int Line, ParsedSectionKind Kind, string Matched)> headings, string? profileText)
    {
        var heading = MatchedHeading(headings, ParsedSectionKind.Profile);
        if (heading is null)
            return new SectionConfidence(
                ParsedSectionKind.Profile, SectionConfidenceLevel.NotFound, ["no heading detected"]);

        return profileText is { Length: > 0 }
            ? new SectionConfidence(
                ParsedSectionKind.Profile, SectionConfidenceLevel.Confident,
                [$"heading '{heading}' matched", "summary text present"])
            : new SectionConfidence(
                ParsedSectionKind.Profile, SectionConfidenceLevel.Degraded,
                [$"heading '{heading}' matched", "empty block"]);
    }

    private static SectionConfidence ListSectionConfidence(
        ParsedSectionKind kind,
        List<(int Line, ParsedSectionKind Kind, string Matched)> headings,
        int count)
    {
        var heading = MatchedHeading(headings, kind);
        if (heading is null)
            return new SectionConfidence(kind, SectionConfidenceLevel.NotFound, ["no heading detected"]);

        return count > 0
            ? new SectionConfidence(
                kind, SectionConfidenceLevel.Confident,
                [$"heading '{heading}' matched", $"{count} entries"])
            : new SectionConfidence(
                kind, SectionConfidenceLevel.Degraded,
                [$"heading '{heading}' matched", "no entries parsed"]);
    }

    private static string? MatchedHeading(
        List<(int Line, ParsedSectionKind Kind, string Matched)> headings,
        ParsedSectionKind kind)
    {
        foreach (var heading in headings)
        {
            if (heading.Kind == kind)
                return heading.Matched;
        }

        return null;
    }

    // ── Helpers / loading ───────────────────────────────────────────────

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static int CountDigits(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
                count++;
        }

        return count;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool TryMapSection(string key, out ParsedSectionKind kind)
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

    private static FrozenSet<string> ToHintSet(
        Dictionary<string, string[]> hints, string key) =>
        hints.TryGetValue(key, out var words)
            ? words.Select(w => w.ToLowerInvariant()).ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;

    private static Lexicon LoadLexicon()
    {
        var assembly = typeof(HeadingDrivenResumeSegmenter).Assembly;
        using var stream = assembly.GetManifestResourceStream(LexiconResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded CV-parsing lexicon missing: {LexiconResourceName}. " +
                "Verify <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var lexicon = JsonSerializer.Deserialize<Lexicon>(reader.ReadToEnd(), LexiconJsonOptions);

        return lexicon
            ?? throw new InvalidOperationException(
                $"Embedded CV-parsing lexicon {LexiconResourceName} deserialized to null.");
    }

    private sealed record Lexicon(
        Dictionary<string, string[]> Headings,
        Dictionary<string, string[]> LanguageHints);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\+?\d[\d\s()\-]{5,}\d", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(
        @"\b(\d{4}|\d{2}/\d{4}|\d{4}-\d{2})\s*[-–—]\s*(\d{4}|\d{2}/\d{4}|\d{4}-\d{2}|nuvarande|pågående|pagaende|present|current|now|idag|nu)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DateRangeRegex();

    [GeneratedRegex(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"[\n,;]", RegexOptions.CultureInvariant)]
    private static partial Regex ListSeparatorRegex();
}
