using System.Text;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// "What did the CV write above its first heading that no contact extractor claimed?" (#844).
///
/// <para><b>The doctrine this class exists to enforce: the engine DESCRIBES, the user CLASSIFIES.</b>
/// Text above the first heading is a summary the user forgot to head, OR a tagline, OR an address
/// block, OR OCR noise — and shape cannot tell them apart. Assigning it to
/// <see cref="ParsedSectionKind.Profile"/> would mint a section identity out of POSITION and SHAPE:
/// the engine inventing a section the user did not write, which is ADR 0071's one absolute
/// prohibition, and a RECOGNITION rule growing a second home (ADR 0107 §3 / ADR 0108 §2 — the
/// 8b.4b Blocker B1 defect class). So this class asserts NOTHING about what the residue IS. It
/// carries it, verbatim and unlabelled, and the user says what it is (ADR 0074
/// propose-and-approve).</para>
///
/// <para><b>Why it cannot fork a rule: it owns none.</b> The residue is defined by SUBTRACTION over
/// recognisers that already existed and already ate the preamble — <c>NameBanners</c> (lexicon),
/// <see cref="MunicipalityLexicon"/> (taxonomy, ADR 0043), and the shapes in
/// <see cref="ContactPatterns"/> (which <see cref="HeadingDrivenResumeSegmenter"/> and
/// <see cref="ContactLocationExtractor"/> now share). No <c>IsProse</c> predicate is written and no
/// threshold is invented. A non-empty residue is therefore NOT a claim that the text is prose — it is
/// the strictly weaker, strictly TRUE statement that <b>the parser could not account for it</b>. That
/// is precisely what A8 needs in order to stop asserting "Profiltext saknas helt." about a summary
/// the user actually wrote.</para>
///
/// <para><b>Fragment-wise, and as a PREDICATE — never an edit.</b> A sidebar/rail CV linearizes its
/// whole contact block onto ONE line ("Anna Andersson | anna@x.se | 070-123 45 67 | Göteborg"), so a
/// line-level rule would leak the entire rail into the carrier. Each fragment is TESTED and, if
/// wholly consumed, CUT — the surviving text is never rewritten. A prose line that merely CONTAINS a
/// contact span ("Kontakta mig gärna på anna@x.se") is not wholly consumed, so it survives WHOLE,
/// e-mail included: we never punch holes in prose we keep.</para>
///
/// <para>Note that <see cref="ContactPatterns.PostalCodeCity"/> is <c>$</c>-anchored. Evaluating it
/// per FRAGMENT is what makes it runnable here at all — inside a rail line it would never reach an
/// end-of-line and would silently miss, leaving "412 58 Göteborg" as a false residue.</para>
///
/// <para><b>The residue does NOT subtract a personnummer, and it therefore CAN carry one</b>
/// (security-auditor, #844). No recogniser here knows that shape: <c>Phone()</c> is anchored on "+"
/// or a "0" trunk prefix, so "811218-9876" matches nothing, and the fragment is KEPT — as the
/// unsure-⇒-keep bias requires. Redacting it inside the carrier is rejected: the carrier is verbatim
/// or it is worthless, and a rewritten preamble is the engine editing the user's words.
///
/// That is safe TODAY, and the reasons are structural, not luck: the import handler scans the WHOLE
/// <c>RawText</c> for personnummer BEFORE the aggregate is persisted, so the carrier is a subset of
/// already-scanned text and adds no undetected surface; the carrier lives inside the same encrypted
/// JSON shadow, on the same row, under the same DEK and the same Art. 17 erasure; it is on no wire
/// (no DTO maps it) and in no log or evidence string; and <c>ParsedResume.EnsureReadyForPromotion</c>
/// REFUSES promotion outright when a personnummer was found.
///
/// <b>Binding on the PR that puts this on the wire:</b> the adopt-as-summary affordance must be
/// fail-closed on <c>Personnummer.Found</c> — the same guard <c>ImportResumeCommandHandler</c>
/// already applies to the source-file capture. Do not surface a preamble from a flagged parse.</para>
///
/// <para><b>A headingless CV has a preamble of the WHOLE document</b> — <c>PreambleLines</c> takes
/// <c>lines.Take(lines.Length)</c> when no heading is detected. For scanned/OCR CVs (the population
/// most likely to carry a personnummer) the carrier is therefore a near-verbatim copy of the CV, up
/// to <see cref="MaxPreambleChars"/>. Proportionate — it is the same data, in the same envelope, with
/// the same lifecycle as <c>RawText</c> — but say it plainly rather than let "text above the first
/// heading" quietly mean "the CV".</para>
/// </summary>
internal static class PreambleResidue
{
    /// <summary>
    /// DoS/pathology bound. A CV with NO headings has a preamble of the WHOLE document
    /// (<c>PreambleLines</c> takes <c>lines.Take(lines.Length)</c>), which would duplicate the entire
    /// CV into the encrypted JSON shadow.
    ///
    /// <para>2000 chars is roughly 300 words — far past any honest summary (the rubric's A8
    /// <c>maxWords</c> is around 100). A document that exceeds it is pathological.</para>
    ///
    /// <para>NOTE, and do not restate this as "lossless" — it is not: truncation here is a REAL
    /// content loss. <c>RawText</c> is not exposed in <c>ParsedResumeDetailDto</c>, so what is cut
    /// here is not recoverable from the guide. Parity with <c>MaxSections</c>, which bears the same
    /// warning for the same reason.</para>
    /// </summary>
    internal const int MaxPreambleChars = 2000;

    /// <summary>
    /// One preamble line after subtraction, split at the line's LAST consumed fragment.
    ///
    /// <para><b>The split is the whole point.</b> A rail line can BOTH end the contact block and carry
    /// the user's summary: "Göteborg | Erfaren undersköterska med tio års erfarenhet" is one line whose
    /// first fragment is contact material and whose second is her profile. A LINE-granular rule drops
    /// that summary — #844's own defect, re-created inside #844's own fix. So the contact block ends at
    /// the last consumed FRAGMENT, and <see cref="After"/> survives it.</para>
    /// </summary>
    /// <param name="Before">Surviving text that precedes the line's last consumed fragment.</param>
    /// <param name="After">
    /// Surviving text that follows it — or, when the line consumed NOTHING, the line VERBATIM (glue and
    /// punctuation and all: a line that recognised nothing must come back byte-for-byte).
    /// </param>
    /// <param name="Consumed">A recogniser claimed at least one whole fragment on this line.</param>
    internal readonly record struct PreambleLine(string Before, string After, bool Consumed);

    /// <summary>
    /// The preamble lines with every span an existing recogniser claims subtracted, in document order
    /// and INCLUDING the lines that emptied (their position is load-bearing).
    ///
    /// <para>Runs BEFORE <c>DetectName</c>, which then reads the residue: on a rail CV the raw line
    /// contains an e-mail, so <c>IsNameLike</c> rejects it outright and the name is lost (a live defect
    /// on main). After subtraction the surviving fragment is just "Anna Andersson", and it is found.
    /// But <c>DetectName</c>'s ANSWER never feeds back into the subtraction — see
    /// <see cref="ToText"/>.</para>
    /// </summary>
    internal static List<PreambleLine> Subtract(
        IReadOnlyList<string> preambleLines, CvParsingLexiconData lexicon)
    {
        ArgumentNullException.ThrowIfNull(preambleLines);
        ArgumentNullException.ThrowIfNull(lexicon);

        var residue = new List<PreambleLine>(preambleLines.Count);
        foreach (var line in preambleLines)
            residue.Add(SubtractLine(line, lexicon));

        return residue;
    }

    /// <summary>
    /// The residue pieces <c>DetectName</c> reads — the fragments, never a rebuilt line.
    ///
    /// <para>Handing it the rebuilt line fabricates a name: on
    /// "Anna Andersson | anna@x.se | 070-… | Göteborg | linkedin.com/in/anna" the surviving text is two
    /// separate items, and joined back together they become the "name"
    /// <c>"Anna Andersson | linkedin.com/in/anna"</c>. Kept apart, the first name-like fragment is
    /// simply "Anna Andersson".</para>
    /// </summary>
    internal static List<string> NameCandidates(IReadOnlyList<PreambleLine> residue)
    {
        ArgumentNullException.ThrowIfNull(residue);

        var candidates = new List<string>(residue.Count * 2);

        foreach (var line in residue)
        {
            // Re-split each surviving segment into its FRAGMENTS. Before/After are REBUILT strings —
            // several surviving fragments re-joined with their original glue — and handing one of those
            // to DetectName fabricates a name: on
            // "Anna Andersson | linkedin.com/in/anna | anna@x.se | Göteborg" the survivors rebuild as
            // "Anna Andersson | linkedin.com/in/anna", and DetectName dutifully reports THAT as her
            // full name. Kept apart, the first name-like fragment is simply "Anna Andersson".
            AddFragments(candidates, line.Before);
            AddFragments(candidates, line.After);
        }

        return candidates;
    }

    private static void AddFragments(List<string> candidates, string segment)
    {
        if (segment.Trim().Length == 0)
            return;

        foreach (var fragment in InlineSeparators.Split(segment))
        {
            var candidate = InlineSeparators.TrimGlue(fragment);
            if (candidate.Length > 0)
                candidates.Add(candidate);
        }
    }

    /// <summary>
    /// The carrier text: the residue MINUS the contact block, joined verbatim. <c>null</c> when nothing
    /// survives — which is what keeps A8's honest <c>Fail</c> ("Profiltext saknas helt.") alive for a
    /// CV that genuinely has no summary.
    ///
    /// <para><b>The contact block is found by POSITION, never by identity</b> (CTO bind, #844 Round 3):
    /// it is the preamble region up to and INCLUDING the last line a recogniser consumed anything on.
    /// Nothing after that point is ever dropped, and if no recogniser consumed anything at all, nothing
    /// is dropped.</para>
    ///
    /// <para><b>Why it cannot be the name.</b> The first design dropped the line <c>DetectName</c>
    /// returned. But <c>DetectName</c> is not a recogniser — it is the heuristic "first substantial line
    /// under 60 characters". Subtracting over a GUESS produced two real defects: a CV with the job title
    /// above the name ("Systemutvecklare / Anna Andersson / mail") deleted the TITLE and carried the
    /// NAME, and a CV whose summary sits above a "Kontakt" heading had the first line of that summary
    /// DELETED — which is #844's own bug, in the exact CV shape #844 is about.
    ///
    /// The root cause is structural and worth stating: <b>a person's name is not recogniser-claimable
    /// and never will be</b> — the vocabulary is unbounded. So a recogniser-only subtraction CANNOT
    /// empty the residue, and cannot both refrain from guessing and preserve A8's Fail arm. Position can
    /// do what identity cannot. This is the same answer <see cref="CvHeadingDetector"/> reached for the
    /// same class of problem: when shape cannot distinguish the wanted from the unwanted, gate on
    /// document POSITION (2026-07-01 bind).</para>
    ///
    /// <para><b>Accepted residual:</b> prose sitting BETWEEN contact lines (a tagline wedged between the
    /// name and the e-mail) is inside the contact block and is dropped. On a CV WITH headings that is
    /// rare, genuinely ambiguous, and bounded by a rule a human can check by eye — and
    /// <paramref name="droppedLineCount"/> reports it as a COUNT so it is measured rather than argued
    /// about.</para>
    ///
    /// <para><b>On a HEADINGLESS CV that bound is much weaker, and this doc will not pretend otherwise.</b>
    /// The preamble is then the WHOLE document, so a contact line sitting BELOW the summary — a
    /// "Referenser: anna@x.se" footer, a bare e-mail line at the end — drags the contact block across
    /// the entire CV and the summary is dropped with it. A8 would then say "Profiltext saknas helt." on
    /// exactly the population #844 exists to serve.
    ///
    /// It is not a regression (before #844 the prose was dropped unconditionally, on every CV), it does
    /// not touch <c>FullName</c> or <c>Location</c> (which read per-line residue and the RAW preamble
    /// respectively), and every dropped line is COUNTED. But the honest description of the bound is
    /// "the last recognised contact line anywhere in scope", not "the top of the CV" — and on a
    /// headingless document those are not the same place. Bounding it (stop the block at the first line
    /// that recognises nothing) is a real design choice with its own failure mode, so it is filed rather
    /// than guessed at here.</para>
    /// </summary>
    /// <param name="droppedLineCount">
    /// How many lines with surviving text were dropped as contact-block material. Structural evidence
    /// only — a count, never the text.
    /// </param>
    internal static string? ToText(IReadOnlyList<PreambleLine> residue, out int droppedLineCount)
    {
        ArgumentNullException.ThrowIfNull(residue);

        droppedLineCount = 0;

        // The contact block ends on the LAST line a recogniser consumed anything on. -1 ⇒ the preamble
        // holds no recognised contact material at all ⇒ nothing is dropped, on any line.
        var lastConsumedLine = -1;
        for (var i = 0; i < residue.Count; i++)
        {
            if (residue[i].Consumed)
                lastConsumedLine = i;
        }

        var kept = new List<string>(residue.Count);

        for (var i = 0; i < residue.Count; i++)
        {
            var (before, after, _) = residue[i];

            // Text preceding this line's last consumed fragment is inside the contact block whenever
            // the block has not yet ended. On the block's LAST line it is still inside it — that is the
            // rail's name ("Anna Andersson | anna@x.se | …").
            // Count per LINE, not per segment. A line can have surviving text both BEFORE and AFTER its
            // last consumed fragment, and counting each would report two dropped lines from one source
            // line. The count is the instrument that justifies the drop being acceptable at all, and an
            // alarm that over-reports is a broken alarm.
            var droppedThisLine = false;

            if (before.Trim().Length > 0)
            {
                if (i <= lastConsumedLine)
                    droppedThisLine = true;
                else
                    kept.Add(before);
            }

            if (after.Trim().Length == 0)
            {
                if (droppedThisLine)
                    droppedLineCount++;

                continue;
            }

            // Text FOLLOWING this line's last consumed fragment has already left the contact block —
            // even on the block's last line. This is what carries "Göteborg | Erfaren undersköterska…",
            // and dropping it would resurrect #844 on the rail layout the issue is actually about.
            if (i < lastConsumedLine)
                droppedThisLine = true;
            else
                kept.Add(after);

            if (droppedThisLine)
                droppedLineCount++;
        }

        var text = string.Join('\n', kept).Trim();
        if (text.Length == 0)
            return null;

        return text.Length <= MaxPreambleChars ? text : Truncate(text);
    }

    // Cut on a line boundary where one exists inside the bound, so the carried text does not end
    // mid-sentence; fall back to a hard cut for a single pathological line.
    //
    // The hard cut must not split a UTF-16 surrogate pair: a lone surrogate is not valid text, and it
    // would be serialised straight into the encrypted JSON shadow. Step back one unit when the cut
    // lands between the halves of an astral character (an emoji in a CV header is not exotic).
    private static string Truncate(string text)
    {
        var head = text[..MaxPreambleChars];

        var lastBreak = head.LastIndexOf('\n');
        if (lastBreak > 0)
            return head[..lastBreak].TrimEnd();

        if (char.IsHighSurrogate(head[^1]))
            head = head[..^1];

        return head.TrimEnd();
    }

    /// <summary>
    /// One line, fragment-wise. Returns the line VERBATIM when no fragment is consumed (the prose
    /// case — untouched, glue and all), the empty string when every fragment is consumed (the rail
    /// case), and otherwise the original line with the consumed fragments and the glue that attached
    /// them cut out — so what survives is still the user's own text, never a rewrite.
    /// </summary>
    private static PreambleLine SubtractLine(string line, CvParsingLexiconData lexicon)
    {
        var empty = new PreambleLine(string.Empty, string.Empty, Consumed: false);

        if (line.Trim().Length == 0)
            return empty;

        // Fragment spans over the ORIGINAL line, so the glue between surviving fragments is preserved
        // byte-for-byte. Regex.Split would discard the offsets.
        var separators = InlineSeparators.Pattern().Matches(line);
        var fragments = new List<(int Start, int End, string GlueBefore)>(separators.Count + 1);

        var position = 0;
        var glue = string.Empty;
        foreach (System.Text.RegularExpressions.Match separator in separators)
        {
            fragments.Add((position, separator.Index, glue));
            glue = line[separator.Index..(separator.Index + separator.Length)];
            position = separator.Index + separator.Length;
        }

        fragments.Add((position, line.Length, glue));

        // An EMPTY fragment — from a leading, trailing or doubled separator — is dropped, but it is NOT
        // a consumption. Counting it as one was a real defect: a prose line ending in a comma produced
        // one empty trailing fragment, which sent the line down the rebuild path and returned it
        // WITHOUT the user's comma. The engine handing back text it silently rewrote is precisely what
        // this class refuses. Empty fragments are NEUTRAL: they decide nothing.
        // Does this line identify itself as CONTACT material? A bare kommun is only the person's home
        // when it sits on a contact line; on any other line it is somebody else's city (an employer's,
        // a school's). See the IsConsumed municipality arm.
        var isRail = CarriesContactSpan(line);

        // "Is this line a bare kommun?" is asked of the RECOGNISER, which owns the split. Deriving it
        // here — by counting surviving fragments — was still a second normaliser: the extractor derived
        // the same fact from the UN-SPLIT line, and a trailing comma was enough to make them disagree
        // ("Göteborg," → consumed by one side, declined by the other → the city reached no field).
        // FRAGMENTATION IS A NORMALISER. One question, one owner, one argument: the raw line.
        var lineIsBareMunicipality = ContactPatterns.TryBareMunicipalityLine(line, out _);

        var consumed = new bool[fragments.Count];
        var lastConsumed = -1;
        var sawRealFragment = false;

        for (var i = 0; i < fragments.Count; i++)
        {
            var text = line[fragments[i].Start..fragments[i].End];
            if (InlineSeparators.TrimGlue(text).Length == 0)
                continue;

            sawRealFragment = true;
            consumed[i] = IsConsumed(text, lexicon, isRail, lineIsBareMunicipality);
            if (consumed[i])
                lastConsumed = i;
        }

        // Nothing but separators (a decorative rule line) — no content, nothing recognised.
        if (!sawRealFragment)
            return empty;

        // NOTHING was consumed ⇒ the line recognised nothing, so it cannot be contact material and it
        // cannot end the contact block. Return it VERBATIM — untouched, glue and punctuation and all.
        // "Kontakta mig gärna på anna@x.se om du vill veta mer" merely CONTAINS a span; it is not MADE
        // of one, and rewriting it would be the engine editing a sentence the user wrote.
        if (lastConsumed < 0)
            return new PreambleLine(string.Empty, line, Consumed: false);

        return new PreambleLine(
            Rebuild(line, fragments, consumed, 0, lastConsumed),
            Rebuild(line, fragments, consumed, lastConsumed + 1, fragments.Count - 1),
            Consumed: true);
    }

    /// <summary>
    /// The surviving (non-consumed) fragments of <paramref name="line"/> in the index range, re-joined
    /// with the ORIGINAL glue that sat between them — so what comes back is the user's own text, never a
    /// rewrite. The glue BEFORE the first surviving fragment is deliberately omitted: it belonged to the
    /// item we just removed, and emitting it would open the carrier with debris from our own
    /// subtraction ("| Erfaren undersköterska…").
    /// </summary>
    private static string Rebuild(
        string line,
        List<(int Start, int End, string GlueBefore)> fragments,
        bool[] consumed,
        int from,
        int to)
    {
        var builder = new StringBuilder();

        for (var i = from; i <= to; i++)
        {
            if (consumed[i])
                continue;

            var text = line[fragments[i].Start..fragments[i].End];
            if (InlineSeparators.TrimGlue(text).Length == 0)
                continue;

            if (builder.Length > 0)
                builder.Append(fragments[i].GlueBefore);

            builder.Append(text);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Is this fragment wholly accounted for by an EXISTING recogniser? Every arm delegates — this
    /// method owns no vocabulary and no shape (see the class remarks).
    /// </summary>
    private static bool IsConsumed(
        string fragment,
        CvParsingLexiconData lexicon,
        bool lineIsContactRail,
        bool lineIsBareMunicipality)
    {
        var candidate = InlineSeparators.TrimGlue(fragment);

        // #428: a CV-title banner ("Curriculum Vitae") is document metadata, not content.
        if (lexicon.NameBanners.Contains(CvParsingLexiconLoader.NormalizeHeading(candidate)))
            return true;

        // A bare kommun — the same taxonomy rung ContactLocationExtractor reads (ADR 0043) — and gated
        // the SAME way that rung is, because the subtraction and the extractor must agree about what
        // counts as contact material. If they disagree, a city gets CLAIMED BY THE SUBTRACTION AND
        // HARVESTED BY NOBODY: absent from Location, absent from the carrier, present in no field at
        // all. That is the silent loss this whole change exists to end, so the two conditions are one
        // rule with two call sites.
        //
        // A kommun counts as contact material when it is the WHOLE line ("Göteborg" under the name), or
        // when it sits on a line that carries a contact span (a rail: "Anna | mail | 070 | Göteborg").
        // On any other line it is somebody else's city — an employer's ("Vårdcentralen, Göteborg"), a
        // school's — and reading it as the person's home would be a fabrication (ADR 0071). This case
        // is not exotic: a CV whose headings the lexicon does not know detects ZERO headings, and then
        // the "preamble" is the WHOLE DOCUMENT, experience lines included.
        if (ContactPatterns.IsBareMunicipality(candidate) && (lineIsBareMunicipality || lineIsContactRail))
            return true;

        // "Ort: Göteborg" — the lexicon's labelled-value rule, whole.
        if (ContactPatterns.TryLabelledValue(candidate, lexicon.LocationLabels, out _))
            return true;

        var (remainder, spanConsumed) = StripContactSpans(candidate);

        // No contact span in this fragment ⇒ it is not contact material, whatever it is. KEEP it
        // (the bound bias: dropping is the bug, keeping is at worst a question the user answers).
        if (!spanConsumed)
            return false;

        var rest = InlineSeparators.TrimGlue(remainder).Trim();
        if (rest.Length == 0)
            return true;

        // The label-prefix rule (FORM, not vocabulary — it never asks what the label MEANS).
        // NARROW BY CONSTRUCTION: a colon-terminated prefix is glue ONLY when nothing but the prefix
        // survives the span subtraction. "E-post: anna@x.se" leaves "E-post:" → consumed. But
        // "Portfolio: se anna@x.se för exempel" leaves "Portfolio: se för exempel" → NOT consumed →
        // the fragment is kept WHOLE, e-mail included. The gate cannot eat prose, because prose
        // leaves more than a prefix behind.
        return rest[^1] == ':';
    }

    /// <summary>
    /// Does this line carry a contact SHAPE (e-mail, phone, postal code + city) anywhere in it?
    ///
    /// <para>This is the "rail signature" — the evidence that a line has already identified itself as
    /// contact material and may therefore be read fragment-wise.
    /// <see cref="ContactLocationExtractor"/> gates its bare-kommun rung on it, and it must be THIS
    /// predicate rather than a second copy: the two would otherwise disagree about what a contact line
    /// is, which is the recognition-rule-with-two-homes defect the whole design exists to make
    /// unavailable.</para>
    /// </summary>
    internal static bool CarriesContactSpan(string line) => StripContactSpans(line).Consumed;

    /// <summary>
    /// Removes the contact SHAPES (e-mail, phone-shaped run, postal code + city) from a fragment,
    /// reporting whether any actually matched. The phone arm re-applies
    /// <see cref="ContactPatterns.IsPhoneShaped"/> so the residue subtracts exactly what the
    /// segmenter calls a phone — pattern and guard travel together, or the recogniser is forked
    /// inside the very act of sharing it.
    /// </summary>
    private static (string Remainder, bool Consumed) StripContactSpans(string fragment)
    {
        var consumed = false;

        var remainder = ContactPatterns.Email().Replace(fragment, _ =>
        {
            consumed = true;
            return " ";
        });

        remainder = ContactPatterns.Phone().Replace(remainder, match =>
        {
            if (!ContactPatterns.IsPhoneShaped(match.Value))
                return match.Value;

            consumed = true;
            return " ";
        });

        remainder = ContactPatterns.PostalCodeCity().Replace(remainder, _ =>
        {
            consumed = true;
            return " ";
        });

        return (remainder, consumed);
    }
}
