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
        // anna@x.se | 070-123 45 67 | Göteborg"). The name recogniser refuses any line carrying an
        // e-mail (and, since #898, any line carrying a digit), so that raw line is refused wholesale
        // and the NAME WOULD BE LOST — a live defect on the most common two-column layout before #844.
        // After subtraction the surviving fragment is just "Anna Andersson", and the name is found.
        // The ordering is therefore not a preference: reading the RAW preamble here would also leak
        // the name into the carrier, which is the thing the carrier must not contain.
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

    /// <summary>
    /// The person's name, at the top of the CV or under an explicit Kontakt heading — or
    /// <c>null</c> when no line is RECOGNISED as one (#898).
    ///
    /// <para><b>It asks a recogniser now, and the difference is the whole point.</b> This method used
    /// to ask <c>IsNameLike</c> — "the first substantial line under 60 characters that is not an
    /// e-mail, a phone or a date" — a heuristic that ALWAYS answers. On the common layout that puts
    /// the job title above the name it answered "Systemutvecklare"; on a CV whose summary sits above a
    /// "Kontakt" heading it answered half that summary. Both were pinned by tests, as known defects.
    /// <see cref="ContactPatterns.TryPersonName"/> owns the question, its fragmentation, its
    /// normalisation and its refusal, so those two layouts now yield the right name.</para>
    ///
    /// <para><b>What that does and does not promise.</b> Refused: prose with a lowercase non-particle
    /// token, anything carrying a digit or a colon, a single token, five or more tokens, and a line
    /// gluing the name to a second item. NOT refused: a 2–4 token title-cased line that happens not to
    /// be a name — see the residual class listed on <see cref="ContactPatterns.TryPersonName"/>. The
    /// claim is "it no longer answers when it does not know", not "it is never wrong".</para>
    ///
    /// <para>No fallback to "the first substantial line" is left behind: deleting it IS the fix. A
    /// refused name is not silent — <c>ContactConfidence</c> drops, <c>ParsedGapSummary.HasFullName</c>
    /// reports the gap to the guide, and B3 warns. The user fills it in (ADR 0040), and nothing is
    /// invented (ADR 0071).</para>
    /// </summary>
    private string? DetectName(
        IReadOnlyList<string> preamble, Dictionary<ParsedSectionKind, string> blocks)
    {
        foreach (var line in preamble)
        {
            if (TryRecogniseName(line, out var name))
                return name;
        }

        if (blocks.TryGetValue(ParsedSectionKind.Contact, out var contactBlock))
        {
            foreach (var line in contactBlock.Split('\n'))
            {
                if (TryRecogniseName(line, out var name))
                    return name;
            }
        }

        return null;
    }

    // #428: a CV-title banner ("Curriculum Vitae", "Meritförteckning", "CV", ...) is document
    // metadata, not the person's name.
    //
    // The banner is asked FIRST, and that ordering is load-bearing for a concrete reason: three
    // shipped banners are TITLE-CASED and 2-token ("Curriculum Vitae", "Cover Letter", "C V"), so
    // TryPersonName would accept them as names if it got there first. #898 made that sharper, not
    // looser — the 2..4-token rule is exactly the shape those banners have. ("Personligt brev" is
    // NOT one of them: its lowercase second token is refused by the recogniser anyway, so listing it
    // here would be an example that does not carry the claim.)
    // (A banner PREFIXED to a name, "CV Anna Andersson", is neither: not a banner by membership, and
    // accepted verbatim by the recogniser. That residual is listed on TryPersonName, not papered over
    // here.)
    //
    // The banner question lives on the lexicon that owns the vocabulary (#898), so the residue and
    // this class cannot ask it two different ways.
    private bool TryRecogniseName(string line, out string name)
    {
        name = string.Empty;
        return !_lexicon.IsNameBanner(line)
            && ContactPatterns.TryPersonName(line, _lexicon.NameParticles, out name);
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

    // #898: LooksLikePhone and LooksLikeDatePeriod lived here to keep a phone line and a period line
    // ("2021 - 2024 Volvo AB") out of the NAME field — the only thing that ever asked them. The
    // recogniser refuses any candidate carrying a digit, which is a superset of both shapes, so
    // keeping them would be two guards that cannot change an outcome. The shapes themselves are
    // untouched: ContactPatterns.Phone/IsPhoneShaped and DatePatterns.DateRange are still the single
    // owners, still read by FirstPhone and by the entry parsers below.

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

    // Header line → (title, organization) best-effort, the split reading the SECOND line when the
    // first carries nothing but a period; falls back to the second line as the organization for
    // the common "Title / Company / Dates" layout.
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

        // #1060 β-1: the line the SPLIT reads must carry fields. A two-column Word template
        // renders the period cell before the role cell, so Lines[0] is the bare period and
        // StripTrailingPeriod reduces it to the empty string. The separator loop below cannot
        // match on "", so the split never ran at all and the fallback handed the whole
        // field-bearing line ("Systemutvecklare - Acme AB") to the ORGANIZATION slot with Title
        // left null. Measured on three corpus arms and in BOTH typed sections, because
        // ParseExperiences and ParseEducations call this one function. The Domain's refusal
        // (Resume.ExperienceRoleRequired) was correct — the Role was genuinely absent, because
        // the parse had destroyed it.
        //
        // Scope, stated narrowly on purpose. This moves ONLY the line the separator loop reads,
        // and only when the first line carries nothing but a period. ONE step, never more: a third
        // line that would have to be searched for is guessing, not relocating. It does NOT move the
        // fallback either — when the next line carries no separator, it is a single field and
        // taking it as the organization is the honest reading; widening the fallback too was tried
        // and measured to hand a description bullet to the organization slot.
        //
        // #1060 β-3 qualified that sentence rather than moving the fallback: "a single field" was
        // only true when the line HAS one. Where Lines[1] carries nothing but a period, taking it
        // was not a degradation but a fabrication, so the fallback now refuses that one case —
        // still without relocating, and still without guessing slot order.
        //
        // It changes WHICH LINE is read, never which SIDE of it is the role. StripTrailingPeriod's
        // bind below (senior-cto-advisor 2026-06-23) reserves slot ORDER as deliberately
        // un-guessed; that is untouched.
        //
        // WHAT THIS TRANSFERS, named because three reviewers reconstructed it independently and the
        // tree did not carry it (bind 2026-08-01 §1-§3). Lines[1] now meets the separator table, so
        // a period-first template whose field line is written company-first ("Klarna AB - Backend-
        // utvecklare") or as "Company, City" ("Verkstaden AB, Göteborg") now PROMOTES with the two
        // slots swapped, where it previously blocked on the missing Role. That is worse for that
        // user and it is accepted, for one reason: before this change the same line was assigned
        // WHOLE to Organization, so the block was standing in front of a FUSED field, not a correct
        // one. Refusing the split would restore the fusion, not the correctness — and deciding
        // which side is the role is the guess the 2026-06-23 bind forbids. The convention is
        // unchanged from what Lines[0] has always done with the same text; this makes the subset
        // consistent with the majority rather than adding a class. Published as a corpus arm
        // (docx-company-first-header) so the cost is measured and not merely argued.
        //
        // The Count guard is NOT redundant beside the emptiness test: a one-line entry whose only
        // line is a bare period reaches here with first == "" (SplitEntries yields such an entry
        // from any non-blank line delimited by blanks, and StripTrailingPeriod consumes a bare
        // "2005 - 2010" or a bare "2005" whole). Without it, Lines[1] throws — and Segment is
        // called unguarded from ImportResumeCommandHandler, so that is a 500 on CV import.
        var splitSource = first;
        if (first.Length == 0 && entry.Lines.Count >= 2)
            splitSource = StripTrailingPeriod(entry.Lines[1]);

        foreach (var separator in TitleOrgSeparators)
        {
            var index = splitSource.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && index + separator.Length < splitSource.Length)
            {
                var title = splitSource[..index].Trim();
                var organization = splitSource[(index + separator.Length)..].Trim();
                return (NullIfEmpty(title), NullIfEmpty(organization));
            }
        }

        // #1060 β-3: a line that carries no fields must not BECOME a field — the mirror of β-1's
        // rule that such a line must not DECIDE the split, and it reuses the predicate the
        // relocation guard already applies earlier in this same method, rather than a new
        // one. Before this, an entry whose first line held no separator
        // glyph took Lines[1] as its organization unconditionally, so a block naming a role and a
        // period and NO employer promoted with the DATE RANGE as the employer name: "2026 - 2026".
        // The engine did not drop a field, it asserted one the source never made, in a document
        // the user sends to employers. Measured by the corpus arm
        // `docx-irreducible-unattributed-experience`.
        //
        // THE GUARD'S SCOPE IS BROADER THAN THAT ARM, and saying so is the point of this
        // paragraph. It fires on "Lines[0] carries no separator AND Lines[1] is nothing but a
        // period" — it does not, and cannot, ask whether an employer sits on Lines[2]. So the
        // layout `Role / Period / Employer` now BLOCKS with the employer physically present in
        // the document. That is accepted and is still an improvement — an honest refusal the user
        // can act on beats a CV asserting she worked at "2026 - 2026" — but it is a wider
        // population than the arm measures, and the arm must not be read as measuring it.
        // Pinned as accepted-and-known by
        // Segment_HeaderLineCarryingNoSeparator_YieldsNoOrganizationEvenWhenTheEmployerIsOnTheThirdLine.
        //
        // BOTH TYPED SECTIONS, because ParseExperiences and ParseEducations call this one
        // function. On the education side the tuple is swapped at the call site, so the org slot
        // is INSTITUTION: the same guard nulls it and the refusal arrives as
        // Resume.EducationInstitutionRequired — a different Domain code from the arm's — and
        // review criterion A10 moves Pass -> Warn. Pinned separately, per β-1's own rule that
        // education symmetry is not decoration.
        //
        // ParseConfidence still reports Confident, before and after: ListSectionConfidence reads
        // only whether a heading matched and how many entries came back, and the entry count is
        // invariant here. Confidence cannot see a fabricated field and does not learn to; the
        // honesty comes entirely from the Domain gate, which is why the point is to REACH it.
        //
        // This is a strict NARROWING and adds no positional assumption. DatePatterns.IsDateOnlyLine
        // is true only when a DatePatterns match runs to the END OF THE LINE with nothing but
        // separators before it, so the guard fires only where the candidate carries no field at
        // all. THAT direction — the absence of false positives — is what the narrowing claim rests
        // on, and it holds.
        //
        // THE CONVERSE DID NOT HOLD WHEN β-3 LANDED, and it was known rather than overlooked. A
        // date line DatePatterns did not model, or one carrying anything after the match, was NOT
        // reduced and still became the organization: "jan 2020 – dec 2024" (no month token in the
        // end-alternation, so only the year matched and " – dec 2024" remained), "2020 – 2024
        // (heltid)", "2005 –" with an open end and no keyword, "2020/01 – 2024/12". The month
        // form was the most consequential by FREQUENCY, not by effect: three of the four also left
        // the segmenter's Period null (measured against ExtractPeriod, which is a different
        // adjudicator from the PeriodParser the test docblock counts against). The honest fix was
        // named as a DatePatterns WIDENING — month names, trailing qualifiers, keyword-less open
        // ends and YYYY/MM.
        //
        // THAT WIDENING LANDED (#1060 road 3) AND THREE OF THE FOUR REDUCED, so none of those three
        // reaches the fallback below and none becomes the organization. Of those three, TWO
        // yield a period (the month-name point form, and the qualifier form via the LINE-level
        // reduction); the third ("2020 –") stays null, because a dangling separator has no end
        // point and inventing one would be the confidently-wrong half of the same defect (ADR 0071,
        // honest-absent). Per form, that is pinned in
        // HeadingDrivenResumeSegmenterTests.Segment_DateLineTheModelNowReaches_…, and the stored
        // value's readability — the property that makes a recovered period worth recovering — in
        // DateModelWideningStoredPeriodTests.
        //
        // THE FOURTH, YYYY/MM ("2020/01 – 2024/12"), REDUCED TOO FOR A WHILE, WAS TAKEN BACK OUT
        // (round 5, senior-cto-advisor bind, decision D′) AND NOW REDUCES AGAIN (ADR 0136). D′ took
        // the slash point out of DateRange because it collided with the Swedish läsår and a
        // mixed-notation form of it stored a value neither PeriodParser nor its callers could read;
        // ADR 0136 gave the LINE question its own grammar instead, so the row reduces without the
        // stored value moving. All four now reach this guard, and the period is honestly absent for
        // this one rather than lifted from a bullet — see ExtractPeriod's veto below.
        //
        // The predicate PROMOTION shipped first (the reduction below now lives in
        // DatePatterns.StripTrailingDate, with DatePatterns.IsDateOnlyLine defined as it, read by
        // ReviewText.DescriptionLines). It was necessary but NOT sufficient, exactly as this
        // paragraph said: it factored today's model into a shared home and inherited its blind
        // spot, so it closed the ReviewText residual and left THIS population to the widening.
        // Two deferrals, not one, and the promotion was the first.
        //
        // THE ORDER WAS LOAD-BEARING, which is the part this paragraph could not say before the
        // measurement existed. On the TWO-LINE layout — the one this fallback is about, where the
        // date row is Lines[1] and therefore the organisation candidate — these four forms reach
        // the review side suppressed only BECAUSE this fallback fabricates them into Organization
        // and ReviewText's organization-equality test then fires on them. Widening the date model
        // first would make Organization correctly null and stop that test firing, releasing the line
        // into ReviewText.ExperienceBullets. The promotion had to land first so the widening extends
        // a real suppression instead of removing an accidental one (senior-cto-advisor bind
        // 2026-08-02, §2).
        //
        // On the THREE-LINE "Title / Company / Dates" layout none of that applied: the employer is
        // real, nothing fabricates the date row, and neither half of ReviewText's union modelled
        // these forms — so the row REACHED the bullet scorer. That escape was MEASURED and pinned in
        // ReviewTextPeriodLineUnionTests, and it is what made the widening close a live hole rather
        // than only preserve a suppression.
        //
        // WHAT THE SCORER DID WITH IT is no longer derived: A1/A2/A6 scored and CITED the row as
        // prose, and on "2020/01 – 2024/12" A1 returned an affirmative Pass noting "kvantifierad
        // uppgift" — the product asserting the user had quantified a result out of her employment
        // dates, CLAUDE.md §5's cited-evidence rule inverted. Measured by the widening under (S1),
        // and closed by it FOR THREE OF THE FOUR FORMS; DateModelWideningReviewSideTests is the
        // adjudicator. The fourth, YYYY/MM, reopened in round 5 (decision D′) and closed in
        // ADR 0136 — on SIX rows, not one: the three MIXED forms ("2018 – 2019/20",
        // "2020 – 2024/12", "2020-06 – 2024/12") were equally unsuppressed, because a trailing
        // "/NN" residue keeps the reduced line non-empty even where DateRange matched a prefix.
        //
        // Relocating the fallback to Lines[2] is a separate decision, refused on TWO measurements:
        // β-1 measured that widening the fallback hands a description bullet to the organization
        // slot, and on this PR's own arm Lines[2] IS the bullet ("Uppdrag åt mindre
        // uppdragsgivare..."), so relocating would do exactly that on the fixture that motivated
        // the change. A third line that has to be searched for is guessing, not relocating. The
        // 2026-06-23 slot-order bind is untouched — nothing here decides WHICH side is the role.
        var orgCandidate = entry.Lines.Count >= 2 ? entry.Lines[1].Trim() : null;
        // Ask the shared PREDICATE, not the reduction: the reduced value is discarded here, so
        // spelling this as `StripTrailingDate(x).Length > 0` would give the question a second
        // spelling in production while the answer has one home. Behaviour-identical by definition
        // (IsDateOnlyLine IS that comparison), and it is what makes a mutation of the predicate
        // itself fall on this reader too.
        var org = orgCandidate is not null && !DatePatterns.IsDateOnlyLine(orgCandidate)
            ? NullIfEmpty(orgCandidate)
            : null;
        return (NullIfEmpty(first.Trim()), org);
    }

    // Remove a TRAILING date range or year from the line the split is about to read, reusing the
    // same patterns ExtractPeriod uses. (Since #1060 β-1 that is Lines[0] or, when Lines[0] carries
    // nothing but a period, Lines[1] — so "header line" is no longer precise.) Only strips when the
    // date/year is at the END (a leading or internal year is likely part of the name, e.g.
    // "Studio 2005 Design", and is left alone). The slot ORDER (which side is the role vs the
    // company) is deliberately NOT guessed — a layout-naive CV may put either in either slot
    // (senior-cto-advisor bind 2026-06-23).
    //
    // The remedy for a swapped pair differs by PATH, and saying so is the whole point of naming it:
    // on the USER-promote path the editable gap-fill is a real approve step (ADR 0040
    // propose-and-approve). On the AUTO-promote path there is no approve step for the promoted
    // content — it is the "spara direkt" mechanism — so the correction available there is ordinary
    // editing of an already-saved CV, which requires the user to notice first. Citing ADR 0040 flat
    // would name a remedy one of the two paths does not have.
    //
    // The reduction itself now lives in DatePatterns.StripTrailingDate, so that
    // DatePatterns.IsDateOnlyLine — which ReviewText.DescriptionLines reads — is defined AS this
    // reduction instead of as a second copy of it. This method stays because the paragraph above
    // is about the SEGMENTER's use of it, not about the reduction.
    private static string StripTrailingPeriod(string line) => DatePatterns.StripTrailingDate(line);

    private static readonly string[] TitleOrgSeparators =
        [" — ", " – ", " - ", ", ", " | ", " @ ", " at ", " på ", " hos "];

    // #428: a full DATE RANGE is unambiguous anywhere in the entry, but a BARE YEAR is only a
    // reliable period signal on the FIRST line (Lines[0]). Scanning the full entry text for a bare
    // year mis-attributes an incidental year in a description bullet ("Migrerade den gamla
    // 1998-stordatorn") as the entry's period. A bare year on a later line is deliberately NOT
    // treated as a period (honest-absent over confidently-wrong; the user fills the gap — but
    // the remedy differs by path, see StripTrailingPeriod above) — ADR 0071.
    //
    // This scope stays Lines[0] and is no longer always the one SplitTitleOrganization reads: since
    // #1060 β-1 that method reads Lines[1] instead when Lines[0] carries nothing but a period. ONE
    // conditional step — it does not scan for a field-bearing line, and an earlier draft of this
    // sentence said it did, which claimed more than the code does. The two scopes answer different
    // questions and the previous cross-reference asserted they were one. Lines[0] is if anything
    // the stronger home for a period after that change: on the very layout that motivated it,
    // Lines[0] IS the period.
    //
    // Residual, priced rather than left implicit: StripTrailingPeriod now also runs on Lines[1], so
    // a trailing BARE YEAR there is cut from the split source while this bare-year branch only ever
    // reads Lines[0]. A full date range is still recovered from entry.Text above; a bare year on
    // line two is not. Honest-absent, and the user fills the gap — on the auto-promote path by
    // editing an already-saved CV, there being no approve step there (StripTrailingPeriod above).
    private static string? ExtractPeriod(Entry entry)
    {
        // THE VETO (#1195, ADR 0136). An entry stating a period this engine recognises as a period
        // but has no authority to DATE gets no period at all — the honest-absent rule this method's
        // own comment above already applies to a bare year on a later line, extended to the one
        // population it did not reach. Without it the two fallbacks below answer confidently and
        // wrongly: DateRange's leftmost scan over entry.Text lifts a range MENTIONED in a
        // description bullet ("2021 – 2023" beside a "2020/01 – 2024/12" date row, ~2 years claimed
        // for a stated ~5), and Year() takes the row's own leading digits ("2019/20 – 2021" → 2019,
        // a zero-length span). Both PARSE, so A4/B6/B7 assess and Pass them.
        //
        // A PATHOLOGICAL SHAPE IS PRICED RATHER THAN GUARDED: an entry carrying BOTH an unreadable
        // date row and a readable one loses both, because the veto is entry-scoped. Refusing is the
        // right direction under ADR 0071, and a rule that had to decide WHICH of two stated periods
        // is the entry's would be guessing. Pinned, not defended against.
        if (entry.Lines.Any(DatePatterns.IsUnreadableDateRow))
            return null;

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
