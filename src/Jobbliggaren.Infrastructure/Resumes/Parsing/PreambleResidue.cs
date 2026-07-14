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
    /// The preamble lines with every span an existing extractor recognises subtracted. Runs BEFORE
    /// <c>DetectName</c>, which then reads the RESIDUE: on a rail CV the raw line contains an e-mail,
    /// so <c>IsNameLike</c> rejects it outright and the name is lost (a live defect on main). After
    /// subtraction the fragment is just "Anna Andersson", and the name is found.
    /// </summary>
    internal static List<string> Subtract(IReadOnlyList<string> preambleLines, CvParsingLexiconData lexicon)
    {
        ArgumentNullException.ThrowIfNull(preambleLines);
        ArgumentNullException.ThrowIfNull(lexicon);

        var residue = new List<string>(preambleLines.Count);
        foreach (var line in preambleLines)
        {
            var remaining = SubtractLine(line, lexicon);
            if (remaining.Trim().Length > 0)
                residue.Add(remaining);
        }

        return residue;
    }

    /// <summary>
    /// The residue as the carrier text: the subtracted lines minus the one <c>DetectName</c> claimed
    /// as the full name, joined verbatim. <c>null</c> when nothing survives — the common case, and
    /// the one that keeps A8's honest <c>Fail</c> ("Profiltext saknas helt.") alive for CVs that
    /// genuinely have no summary.
    /// </summary>
    internal static string? ToText(IReadOnlyList<string> residue, string? fullName)
    {
        ArgumentNullException.ThrowIfNull(residue);

        var kept = new List<string>(residue.Count);
        var nameDropped = false;

        foreach (var line in residue)
        {
            // Drop the name ONCE — a person may legitimately write their own name again in a
            // summary, and the second occurrence is content, not the contact field.
            if (!nameDropped && fullName is { Length: > 0 } && line.Trim() == fullName)
            {
                nameDropped = true;
                continue;
            }

            kept.Add(line);
        }

        var text = string.Join('\n', kept).Trim();
        if (text.Length == 0)
            return null;

        return text.Length <= MaxPreambleChars ? text : Truncate(text);
    }

    // Cut on a line boundary where one exists inside the bound, so the carried text does not end
    // mid-sentence; fall back to a hard cut for a single pathological line.
    private static string Truncate(string text)
    {
        var head = text[..MaxPreambleChars];
        var lastBreak = head.LastIndexOf('\n');
        return (lastBreak > 0 ? head[..lastBreak] : head).TrimEnd();
    }

    /// <summary>
    /// One line, fragment-wise. Returns the line VERBATIM when no fragment is consumed (the prose
    /// case — untouched, glue and all), the empty string when every fragment is consumed (the rail
    /// case), and otherwise the original line with the consumed fragments and the glue that attached
    /// them cut out — so what survives is still the user's own text, never a rewrite.
    /// </summary>
    private static string SubtractLine(string line, CvParsingLexiconData lexicon)
    {
        if (line.Trim().Length == 0)
            return string.Empty;

        // Fragment spans over the ORIGINAL line, so the glue between surviving fragments can be
        // preserved byte-for-byte. Regex.Split would discard the offsets.
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

        var consumed = new bool[fragments.Count];
        var anyConsumed = false;
        var allConsumed = true;

        for (var i = 0; i < fragments.Count; i++)
        {
            consumed[i] = IsConsumed(line[fragments[i].Start..fragments[i].End], lexicon);
            anyConsumed |= consumed[i];
            allConsumed &= consumed[i];
        }

        // The prose case. Return the line UNTOUCHED — this is the invariant that keeps the engine
        // from ever handing back text it rewrote.
        if (!anyConsumed)
            return line;

        if (allConsumed)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var (start, end, glueBefore) in fragments.Where((_, i) => !consumed[i]))
        {
            if (builder.Length > 0)
                builder.Append(glueBefore);

            builder.Append(line[start..end]);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Is this fragment wholly accounted for by an EXISTING recogniser? Every arm delegates — this
    /// method owns no vocabulary and no shape (see the class remarks).
    /// </summary>
    private static bool IsConsumed(string fragment, CvParsingLexiconData lexicon)
    {
        var candidate = InlineSeparators.TrimGlue(fragment);
        if (candidate.Length == 0)
            return true;

        // #428: a CV-title banner ("Curriculum Vitae") is document metadata, not content.
        if (lexicon.NameBanners.Contains(CvParsingLexiconLoader.NormalizeHeading(candidate)))
            return true;

        // A bare kommun — the same taxonomy rung ContactLocationExtractor reads (ADR 0043).
        if (MunicipalityLexicon.IsMunicipality(candidate))
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
