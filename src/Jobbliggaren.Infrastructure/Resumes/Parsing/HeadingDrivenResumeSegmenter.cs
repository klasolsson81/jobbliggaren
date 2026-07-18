using System.Buffers;
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
internal sealed partial class HeadingDrivenResumeSegmenter(CvParsingLexiconData lexicon) : IResumeSegmenter
{
    private const int MaxSkills = 200;
    private const int MaxLanguages = 50;
    private const int MaxEntries = 100;

    // DoS bound, parity with MaxSkills/MaxLanguages. NOTE: unlike those, truncation here is a
    // real (if pathological) content loss — RawText is NOT exposed in ParsedResumeDetailDto, so a
    // dropped section is not recoverable from the guide. 30 sections is far past any honest CV;
    // a document that exceeds it is adversarial, and refusing to allocate for it is the right
    // call. Do not restate this as "lossless" — it is not.
    private const int MaxSections = 30;

    // The lexicon, injected (8b.4a). This class used to load and shape the JSON itself in a static
    // ctor. Two things were wrong with that: the load fired on FIRST PARSE — inside a user's CV
    // import, so a broken asset was an HTTP 500 rather than a failed boot — and the class was
    // untestable against anything but the shipped asset. It is now ONE DI-registered value, shared
    // with ICvParsingLexicon, so RECOGNITION and section-id RESOLUTION cannot disagree.
    private readonly CvParsingLexiconData _lexicon =
        lexicon ?? throw new ArgumentNullException(nameof(lexicon));

    public ResumeSegmentationResult Segment(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        var lines = SplitLines(rawText);
        var headings = CvHeadingDetector.Detect(lines, _lexicon);
        var (blocks, freeSections) = BuildSectionBlocks(lines, headings);
        var preamble = PreambleLines(lines, headings);
        var language = DetectLanguage(rawText);

        var email = FirstEmail(rawText);
        var phone = FirstPhone(rawText);

        // #844: the residue runs BEFORE DetectName, and DetectName reads the RESIDUE.
        //
        // A sidebar/rail CV linearizes its contact block onto ONE line ("Anna Andersson |
        // anna@x.se | 070-123 45 67 | Göteborg"). IsNameLike rejects any line matching EmailRegex,
        // so that raw line was rejected wholesale and the NAME WAS LOST — a live defect on the most
        // common two-column layout. After subtraction the surviving fragment is just "Anna
        // Andersson", and the name is found. The ordering is therefore not a preference: reading the
        // RAW preamble here would also leak the name into the carrier, which is the thing the
        // carrier must not contain.
        var residue = PreambleResidue.Subtract(preamble, _lexicon);
        var fullName = DetectName(PreambleResidue.NameCandidates(residue), blocks);

        // #815: Location was `null` here, hardcoded — city extraction did not exist, so every CV
        // ever imported reported "ort saknas" even when the CV stated the city plainly. The bare-
        // city rung reads ONLY contact scope (contact block + preamble): an employer's city inside
        // an experience entry must never become the person's home (see ContactLocationExtractor).
        //
        // NOTE the RAW preamble, deliberately — NOT the residue. The residue SUBTRACTS the bare
        // kommun (it is one of its consumption terms), so feeding it here would leave the city
        // claimed by the subtraction and harvested by nobody.
        var contactScope = ContactScopeLines(preamble, blocks);
        var location = ContactLocationExtractor.Extract(rawText, contactScope, _lexicon.LocationLabels);

        var contact = new ParsedContact(fullName, email, phone, location);

        // #844: the carrier. Text the CV wrote above its first heading that no contact extractor
        // claimed — verbatim and UNCLASSIFIED. The engine does not call it a profile: shape cannot
        // tell a heading-less summary from a tagline, an address block or OCR noise, and guessing
        // would be the engine inventing a section the user did not write (ADR 0071). It is carried
        // so the user can decide (ADR 0074) — and so A8 can stop reporting "Profiltext saknas helt."
        // about a summary she did write.
        // The contact block is subtracted by POSITION, not by DetectName's answer (CTO bind, Round 3).
        // A person's name is not recogniser-claimable and never will be, so a recogniser-only
        // subtraction cannot empty the residue — and the first design papered over that by deleting the
        // line DetectName GUESSED was the name. That guess deleted a job title on one common layout and
        // the first line of the user's summary on another. Position can do what identity cannot.
        var preambleText = PreambleResidue.ToText(residue, out var droppedLineCount);

        var profileText = SectionText(blocks, ParsedSectionKind.Profile);
        var experiences = ParseExperiences(blocks);
        var educations = ParseEducations(blocks);
        var skillsParse = ParseList(blocks, ParsedSectionKind.Skills, MaxSkills);
        var languagesParse = ParseList(blocks, ParsedSectionKind.Languages, MaxLanguages);

        // #856: an over-long token the segmenter could not atomise is routed OUT of the typed list
        // into a free section carrying the recognised heading verbatim — the prose is preserved and
        // shown back (no truncation/invention/drop, ADR 0071) instead of poisoning a scored chip.
        // Appended before content is built so it rides the same Sections surface; see
        // AppendRoutedSection for why the MaxSections cap must not gate it.
        AppendRoutedSection(freeSections, headings, ParsedSectionKind.Skills, skillsParse.Routed);
        AppendRoutedSection(freeSections, headings, ParsedSectionKind.Languages, languagesParse.Routed);

        var skills = skillsParse.Kept;
        var languages = languagesParse.Kept;

        var content = new ParsedResumeContent(
            contact, profileText, experiences, educations, skills, languages, freeSections,
            preambleText);

        var sections = new List<SectionConfidence>
        {
            ContactConfidence(contact),
            ProfileConfidence(headings, profileText, preambleText, droppedLineCount),
            ListSectionConfidence(ParsedSectionKind.Experience, headings, experiences.Count),
            ListSectionConfidence(ParsedSectionKind.Education, headings, educations.Count),
            ListSectionConfidence(ParsedSectionKind.Skills, headings, skills.Count, skillsParse.Routed.Count),
            ListSectionConfidence(
                ParsedSectionKind.Languages, headings, languages.Count, languagesParse.Routed.Count),
        };

        var confidence = ParseConfidence.FromSections(sections);
        return new ResumeSegmentationResult(content, language, confidence);
    }

    // ── Heading detection ───────────────────────────────────────────────

    // A detected section heading: its line index, section kind, the normalised matched form
    // (structural evidence only — never PII), and any content carried inline on the same line
    // after a colon ("Kompetenser: C#, …" → InlineContent "C#, …"). Inline content becomes the
    // section block's first content line (#421, #252-class).
    /// <param name="Kind">
    /// The typed section this heading opens, or <c>null</c> for a FREE section (#815 — "Projekt",
    /// "Referenser", …). A free heading terminates the preceding section exactly like a typed one;
    /// the difference is only where its body goes, never whether it counts as a boundary.
    /// </param>
    /// <param name="Heading">
    /// The heading line VERBATIM (trimmed, trailing colon removed). Free sections carry this to the
    /// user as content, so casing and wording are preserved — "PROJEKT" is not "projekt". The
    /// normalised <c>Matched</c> form remains structural evidence only.
    /// </param>
    // Heading DETECTION (whole-line + the boundary-gated inline form, #421) lives in
    // CvHeadingDetector (8b.4b): the order analyzer must observe EXACTLY the headings this
    // segmenter segmented on, or it silently reports an order the document does not have. Sharing
    // the normaliser was not enough — the drift was in the detection rule.

    // THE normalizer lives with the lexicon it normalizes (8b.4a). Every heading the lexicon
    // STORES and every heading line a CV PRESENTS goes through this one function — including the
    // TYPED variants, which previously got a bare ToLowerInvariant() and would therefore have gone
    // DEAD if anyone had added one with a trailing colon or a double space.
    private static string NormalizeHeading(string line) => CvParsingLexiconLoader.NormalizeHeading(line);

    /// <summary>
    /// Splits the document into the six TYPED blocks (keyed by kind) and the FREE sections
    /// (an ordered list, #815).
    ///
    /// <para>The two destinations are the point. Typed blocks are keyed by kind and a repeated
    /// heading concatenates — fine, because "Erfarenhet" means one thing. Free sections must NOT be
    /// keyed by anything: keying them (e.g. on a single <c>ParsedSectionKind.Other</c>) would fuse
    /// PROJEKT and REFERENSER into one concatenated block and keep only the enum token, throwing
    /// away the headings the user wrote. That would recreate this very bug one layer down. So free
    /// sections are appended in document order, never merged — two sections with the SAME heading
    /// stay two sections.</para>
    /// </summary>
    private static (Dictionary<ParsedSectionKind, string> Typed, List<ParsedSection> Free)
        BuildSectionBlocks(string[] lines, List<DetectedHeading> headings)
    {
        var blocks = new Dictionary<ParsedSectionKind, string>();
        var free = new List<ParsedSection>();

        for (var h = 0; h < headings.Count; h++)
        {
            var start = headings[h].Line + 1;
            var end = h + 1 < headings.Count ? headings[h + 1].Line : lines.Length;

            IEnumerable<string> bodyLines =
                start < end ? lines.Skip(start).Take(end - start) : [];

            // Inline "heading: content" (#421): the remainder after the colon is the block's
            // FIRST content line, ahead of any lines that follow the heading.
            if (headings[h].InlineContent is { Length: > 0 } inlineContent)
                bodyLines = bodyLines.Prepend(inlineContent);

            var block = string.Join('\n', bodyLines).Trim();

            if (headings[h].Kind is { } kind)
            {
                // Same typed heading twice ⇒ concatenate the blocks deterministically.
                blocks[kind] = blocks.TryGetValue(kind, out var existing)
                    ? string.Concat(existing, "\n", block).Trim()
                    : block;
                continue;
            }

            // Free section. An empty body still counts: the user wrote the heading, and dropping
            // it would be us deciding their section was worthless.
            if (free.Count >= MaxSections)
                continue;

            free.Add(new ParsedSection(headings[h].Heading, BuildSectionEntries(block)));
        }

        return (blocks, free);
    }

    /// <summary>
    /// A free section's body → entries, reusing the SAME blank-line rule as Experience/Education
    /// (DRY — one owner of what an "entry" is). The first line becomes the entry Title only when
    /// the entry has more than one line; a lone line, or a bullet, is content, and the parser will
    /// not promote it into a title it did not write.
    /// </summary>
    private static List<ParsedSectionEntry> BuildSectionEntries(string block)
    {
        var entries = new List<ParsedSectionEntry>();
        if (block.Length == 0)
            return entries;

        foreach (var entry in SplitEntries(block))
        {
            if (entries.Count >= MaxEntries)
                break;

            var lines = entry.Lines;
            if (lines.Count > 1 && !IsBulletLine(lines[0]))
                entries.Add(new ParsedSectionEntry(lines[0], [.. lines.Skip(1)]));
            else
                entries.Add(new ParsedSectionEntry(null, [.. lines]));
        }

        return entries;
    }

    private static bool IsBulletLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && BulletMarkers.Contains(trimmed[0]);
    }

    // Bullet glyphs a CV realistically uses. A bulleted first line is content, never a title.
    private static readonly SearchValues<char> BulletMarkers =
        SearchValues.Create(['-', '*', '•', '–', '—', '·', '●', '▪']);

    private static List<string> PreambleLines(
        string[] lines,
        List<DetectedHeading> headings)
    {
        var firstHeading = headings.Count > 0 ? headings[0].Line : lines.Length;
        return lines.Take(firstHeading).ToList();
    }

    // ── Field extraction ────────────────────────────────────────────────

    private static string? SectionText(
        Dictionary<ParsedSectionKind, string> blocks, ParsedSectionKind kind) =>
        blocks.TryGetValue(kind, out var text) && text.Length > 0 ? text : null;

    private string? DetectName(
        IReadOnlyList<string> preamble, Dictionary<ParsedSectionKind, string> blocks)
    {
        // The name is conventionally the first substantial line at the top of a CV
        // (or under an explicit Kontakt heading) — not an e-mail/phone/heading line.
        foreach (var line in preamble)
        {
            if (IsNameBanner(line))
                continue;
            if (IsNameLike(line))
                return line.Trim();
        }

        if (blocks.TryGetValue(ParsedSectionKind.Contact, out var contactBlock))
        {
            foreach (var line in contactBlock.Split('\n'))
            {
                if (IsNameBanner(line))
                    continue;
                if (IsNameLike(line))
                    return line.Trim();
            }
        }

        return null;
    }

    // #428: a CV-title banner ("Curriculum Vitae", "Meritförteckning", "CV", ...) at the top of
    // a CV is document metadata, not the person's name — skip it so DetectName does not return
    // the banner as FullName (which also inflated ContactConfidence via hasName). Banners are
    // versioned lexicon data (§5), matched after the same NormalizeHeading pass headings use.
    private bool IsNameBanner(string line) => _lexicon.NameBanners.Contains(NormalizeHeading(line));

    private static bool IsNameLike(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length is 0 or > 60)
            return false;

        if (EmailRegex().IsMatch(trimmed) || LooksLikePhone(trimmed) || LooksLikeDatePeriod(trimmed))
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

    // #844: the digit-count guard moved to ContactPatterns WITH its pattern. A pattern and its guard
    // are one recogniser; sharing only the regex would let PreambleResidue subtract things this
    // segmenter does not call a phone.
    private static bool IsPhoneShaped(string candidate) => ContactPatterns.IsPhoneShaped(candidate);

    private static string? FirstPhone(string text)
    {
        foreach (Match candidate in PhoneRegex().Matches(text))
        {
            if (IsPhoneShaped(candidate.Value))
                return candidate.Value.Trim();
        }

        return null;
    }

    private static bool LooksLikePhone(string line)
    {
        var match = PhoneRegex().Match(line);
        return match.Success && IsPhoneShaped(match.Value);
    }

    /// <summary>
    /// A line carrying a date RANGE ("2021 - 2024", "2005 - nu") is a CV period, never a person's
    /// name. Before #815 this was enforced by ACCIDENT: the old phone pattern matched any digit
    /// run, so date lines were rejected as "phone-like". Tightening the phone pattern removed that
    /// side effect, which would have let a line like "2021 - 2024 Volvo AB" — it has letters, so it
    /// clears the letter check — become a name candidate. The rejection now rests on the date shape
    /// it was always really about, and DatePatterns is the single owner of that shape (no drift).
    /// </summary>
    private static bool LooksLikeDatePeriod(string line) => DateRangeRegex().IsMatch(line);

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
            result.Add(new ParsedExperience(title, organization, ExtractPeriod(entry), entry.Text));
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
            result.Add(new ParsedEducation(institution, degree, ExtractPeriod(entry), entry.Text));
        }

        return result;
    }

    // #856: the outcome of parsing a typed list block. Kept = the short atoms that stay skills/
    // languages (the scored units). Routed = tokens too long to BE an atom — prose the segmenter
    // could not split (the line carried no separator glyph). A named struct, not a tuple, because
    // Segment consumes it twice (Skills + Languages) and routing is a first-class part of the result.
    private readonly record struct ListParse(IReadOnlyList<string> Kept, IReadOnlyList<string> Routed);

    private static ListParse ParseList(
        Dictionary<ParsedSectionKind, string> blocks, ParsedSectionKind kind, int cap)
    {
        var kept = new List<string>();
        var routed = new List<string>();
        if (!blocks.TryGetValue(kind, out var block) || block.Length == 0)
            return new ListParse(kept, routed);

        foreach (var token in ListSeparatorRegex().Split(block))
        {
            var trimmed = token.Trim().TrimStart('•', '-', '*', '·', '–', '—', '|').Trim();
            if (trimmed.Length == 0)
                continue;

            // #856: an over-long token is not a skill/language — it is a sentence the segmenter
            // failed to split (no separator glyph on the line). Emitting it as a chip poisons the
            // scored atom the matcher scores. Route it out VERBATIM (Segment places it in a free
            // section) rather than truncate (invention) or drop (#849). The threshold is the domain's
            // own scored-atom bound — a token Resume.ValidateContent would reject as a name never
            // becomes a chip (Skill.NameMaxLength, #855). Strict '>': exactly-max stays an atom, in
            // lockstep with the domain cap (== max is accepted there). Routed is bounded by MaxEntries
            // so the rescue cannot itself become a DoS vector.
            if (trimmed.Length > Skill.NameMaxLength)
            {
                if (routed.Count < MaxEntries)
                    routed.Add(trimmed);
                continue;
            }

            kept.Add(trimmed);
            if (kept.Count >= cap)
                break;
        }

        return new ListParse(kept, routed);
    }

    // #856: build a free section for the tokens routed out of a typed list, keyed to the recognised
    // heading VERBATIM (the user's own line, casing preserved — parity with the free sections
    // BuildSectionBlocks makes). One section per (kind, block); each routed token is its own entry
    // with no title (a lone line is content, never a title the parser invents — #815 / ADR 0071).
    //
    // Appended UNCONDITIONALLY — the MaxSections cap deliberately does NOT gate this. That cap bounds
    // how many arbitrary DOCUMENT headings the parser will allocate sections for (a DoS bound where
    // truncation is an accepted, if pathological, loss). Re-applying it here would SILENTLY DROP the
    // very prose this fix rescues — the exact ADR 0071 / #849 defect #856 exists to close. The add is
    // bounded anyway: ParseList runs once per typed list kind, so at most two routed sections, each
    // with at most MaxEntries entries.
    private static void AppendRoutedSection(
        List<ParsedSection> freeSections,
        List<DetectedHeading> headings,
        ParsedSectionKind kind,
        IReadOnlyList<string> routed)
    {
        if (routed.Count == 0)
            return;

        var heading = VerbatimHeading(headings, kind);
        if (heading is null)
            return; // unreachable in practice: routed is non-empty only when a typed block existed,
                    // which requires a detected heading of that kind — but never NRE on an invariant.

        var entries = new List<ParsedSectionEntry>(routed.Count);
        foreach (var line in routed)
            entries.Add(new ParsedSectionEntry(null, [line]));

        freeSections.Add(new ParsedSection(heading, entries));
    }

    private static string? VerbatimHeading(List<DetectedHeading> headings, ParsedSectionKind kind)
    {
        foreach (var heading in headings)
        {
            if (heading.Kind == kind)
                return heading.Heading;
        }

        return null;
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

    // #428: a full DATE RANGE is unambiguous anywhere in the entry, but a BARE YEAR is only a
    // reliable period signal on the HEADER line (Lines[0]) — the same scope StripTrailingPeriod
    // trusts. Scanning the full entry text for a bare year mis-attributes an incidental year in a
    // description bullet ("Migrerade den gamla 1998-stordatorn") as the entry's period. A bare
    // year on a non-header line is deliberately NOT treated as a period (honest-absent over
    // confidently-wrong; the user fills the gap via ADR 0040 propose-and-approve) — ADR 0071.
    private static string? ExtractPeriod(Entry entry)
    {
        var range = DateRangeRegex().Match(entry.Text);
        if (range.Success)
            return range.Value.Trim();

        if (entry.Lines.Count == 0)
            return null;

        var year = YearRegex().Match(entry.Lines[0]);
        return year.Success ? year.Value : null;
    }

    // ── Language detection (F4-8 scope; English analysis deferred to F4-9) ──

    private ResumeLanguage DetectLanguage(string text)
    {
        var swedish = 0;
        var english = 0;
        foreach (var word in Tokenize(text))
        {
            if (_lexicon.SwedishHints.Contains(word))
                swedish++;
            if (_lexicon.EnglishHints.Contains(word))
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

    /// <summary>
    /// #844: when no Profil heading was detected, the level stays <c>NotFound</c> — that is literally
    /// true, and stretching it to <c>Degraded</c> would corrupt that level's meaning ("heading
    /// matched, empty block"). What changes is the EVIDENCE: if unclassified text was carried from
    /// above the first heading, say so, because "no heading detected" alone let the user believe her
    /// summary was simply not there.
    ///
    /// <para>The evidence is a COUNT, never the text. <c>ParseConfidence</c>'s contract is that its
    /// evidence cites STRUCTURE, never CV content — the confidence block is not a PII channel.</para>
    /// </summary>
    private static SectionConfidence ProfileConfidence(
        List<DetectedHeading> headings, string? profileText, string? preambleText, int droppedLineCount)
    {
        var heading = MatchedHeading(headings, ParsedSectionKind.Profile);
        if (heading is null)
        {
            var evidence = new List<string> { "no heading detected" };

            if (preambleText is { Length: > 0 })
            {
                var lineCount = preambleText.Split('\n').Length;
                evidence.Add($"{lineCount} unclassified line(s) carried from above the first heading");
            }

            // The contact-block drop is the one place this engine deliberately discards a line the user
            // wrote (a tagline wedged between the name and the e-mail would land here). It is rare and
            // it is bounded, but it must be MEASURED rather than argued about — so it is counted, in
            // the open, every time it happens. A count, never the text: this evidence rides the
            // parse_confidence column, which is NOT encrypted.
            if (droppedLineCount > 0)
                evidence.Add($"text dropped from {droppedLineCount} line(s) as contact-block material");

            return new SectionConfidence(
                ParsedSectionKind.Profile, SectionConfidenceLevel.NotFound, evidence);
        }

        return profileText is { Length: > 0 }
            ? new SectionConfidence(
                ParsedSectionKind.Profile, SectionConfidenceLevel.Confident,
                [$"heading '{heading}' matched", "summary text present"])
            : new SectionConfidence(
                ParsedSectionKind.Profile, SectionConfidenceLevel.Degraded,
                [$"heading '{heading}' matched", "empty block"]);
    }

    // <paramref name="routedCount"/> (#856): tokens too long to be a scored atom that were routed
    // out to a free section (Skills/Languages only; 0 for Experience/Education). The evidence must
    // say so, because a block whose ONLY content was over-long parses to 0 atoms — reporting a bare
    // "no entries parsed" would blame the user for prose the segmenter chose to relocate. The note is
    // a structural COUNT, never the CV text: SectionConfidence.Evidence rides the unencrypted
    // parse_confidence column (parity with ProfileConfidence's dropped-line count).
    private static SectionConfidence ListSectionConfidence(
        ParsedSectionKind kind,
        List<DetectedHeading> headings,
        int count,
        int routedCount = 0)
    {
        var heading = MatchedHeading(headings, kind);
        if (heading is null)
            return new SectionConfidence(kind, SectionConfidenceLevel.NotFound, ["no heading detected"]);

        if (count > 0)
        {
            var evidence = new List<string> { $"heading '{heading}' matched", $"{count} entries" };
            if (routedCount > 0)
                evidence.Add(RoutedEvidence(routedCount));
            return new SectionConfidence(kind, SectionConfidenceLevel.Confident, evidence);
        }

        return routedCount > 0
            ? new SectionConfidence(
                kind, SectionConfidenceLevel.Degraded,
                [$"heading '{heading}' matched", RoutedEvidence(routedCount)])
            : new SectionConfidence(
                kind, SectionConfidenceLevel.Degraded,
                [$"heading '{heading}' matched", "no entries parsed"]);
    }

    private static string RoutedEvidence(int routedCount) =>
        $"{routedCount} over-long entr{(routedCount == 1 ? "y" : "ies")} routed to a free section";

    private static string? MatchedHeading(
        List<DetectedHeading> headings,
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

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>
    /// The lines a bare city name may be read from: the preamble (everything before the first
    /// heading — where a rail-style CV puts its contact details) plus the Contact block itself.
    /// Deliberately NOT the whole document: see ContactLocationExtractor for why that scope is the
    /// honesty guard, not an optimisation.
    /// </summary>
    private static List<string> ContactScopeLines(
        List<string> preamble, Dictionary<ParsedSectionKind, string> blocks)
    {
        var scope = new List<string>(preamble);

        if (blocks.TryGetValue(ParsedSectionKind.Contact, out var contactBlock))
            scope.AddRange(SplitLines(contactBlock));

        return scope;
    }

    // #487 / #844: the shared-form aliases. The date shapes moved to DatePatterns (so the review
    // engine masks the SAME dates this segmenter extracts); the CONTACT shapes and the inline-glue
    // glyphs moved to ContactPatterns / InlineSeparators (so PreambleResidue SUBTRACTS exactly what
    // this segmenter and ContactLocationExtractor RECOGNISE). One knowledge piece, one owner — a
    // second copy is how a recognition rule grows two homes that disagree (8b.4b, Blocker B1).
    // Local aliases keep every existing call site unchanged.
    private static Regex EmailRegex() => ContactPatterns.Email();

    private static Regex PhoneRegex() => ContactPatterns.Phone();

    private static Regex DateRangeRegex() => DatePatterns.DateRange();

    private static Regex YearRegex() => DatePatterns.Year();

    private static Regex ListSeparatorRegex() => InlineSeparators.Pattern();
}
