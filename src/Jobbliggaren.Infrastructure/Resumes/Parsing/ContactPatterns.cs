using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministic CV CONTACT-token patterns — "what an e-mail / a phone number / a Swedish postal
/// code + city / a labelled contact value looks like" — with ONE owner (#844).
///
/// <para>Promoted here for the same reason <see cref="DatePatterns"/> and <c>PeriodParser</c> were:
/// the shapes were private to two different classes (<see cref="HeadingDrivenResumeSegmenter"/> held
/// e-mail/phone, <see cref="ContactLocationExtractor"/> held postal-code and the label rule), and
/// #844's preamble residue needs to SUBTRACT exactly what those two RECOGNISE. A third copy would be
/// a forked form rule — the 8b.4b Blocker B1 defect class (a recognition rule with two homes that
/// disagree), reproduced one layer down. Sharing makes divergence impossible: the residue's precision
/// is, by construction, identical to the extractors' precision — including their blind spots.</para>
///
/// <para><b>A pattern travels with its guard.</b> <see cref="IsPhoneShaped"/> and the length cap in
/// <see cref="TryLabelledValue"/> are part of the RECOGNISER, not decoration around it. Sharing only
/// the regex and leaving the guard behind would fork the recogniser inside the very act of sharing
/// it — the residue would subtract things the segmenter does not call a phone.</para>
///
/// <para>FORM lives in C#, vocabulary lives in the lexicon (ADR 0108 §2). These are shapes; the label
/// VOCABULARY (<c>contactLabels.location</c>) stays versioned lexicon data and is passed in.</para>
/// </summary>
internal static partial class ContactPatterns
{
    // Kept byte-identical to the patterns the two owners previously held, so the promotion is
    // behaviour-preserving by inspection (the DatePatterns precedent).
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    internal static partial Regex Email();

    // #815: anchored on "+" (international) or a "0" trunk prefix — that anchor is what separates a
    // phone from a date range, a postal code ("412 58") and an org number ("556677-8899"). The digit
    // COUNT is validated in IsPhoneShaped rather than in the pattern, so the rule stays readable.
    // The dash class covers the Unicode dash family, written as escapes so no literal glyph enters
    // the source.
    [GeneratedRegex(@"(?:\+|\b0)[\d\s()\-\u2010-\u2015]{5,}\d", RegexOptions.CultureInvariant)]
    internal static partial Regex Phone();

    // Swedish postnummer: five digits, conventionally written "412 58", city on the same line.
    // NOTE the "$" anchor + Multiline: this matches a line (or, under fragment-wise evaluation, a
    // FRAGMENT) that ENDS with the city. PreambleResidue therefore evaluates it per fragment, never
    // mid-line — see the note on that class.
    [GeneratedRegex(
        @"\b\d{3}\s?\d{2}\s+(?<city>\p{Lu}[\p{L}\-\s]{1,39})$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    internal static partial Regex PostalCodeCity();

    /// <summary>
    /// The shortest and longest digit count a phone number may carry. The floor rejects short digit
    /// runs; the ceiling is E.164's maximum, and it matters: without it a long numeric run (an ID, a
    /// reference number) starting with 0 would be accepted as a phone.
    /// </summary>
    private const int MinPhoneDigits = 7;
    private const int MaxPhoneDigits = 15;

    /// <summary>
    /// True when a <see cref="Phone"/> candidate carries a phone-plausible digit count. Part of the
    /// recogniser — see the class remarks.
    /// </summary>
    internal static bool IsPhoneShaped(string candidate)
    {
        var digits = 0;
        foreach (var c in candidate)
        {
            if (char.IsAsciiDigit(c))
                digits++;
        }

        return digits is >= MinPhoneDigits and <= MaxPhoneDigits;
    }

    /// <summary>
    /// A place name, not a sentence. Sweden's longest municipality name is well under this; the cap
    /// exists so a labelled line carrying prose ("Ort: har bott i Göteborg sedan 2005") cannot be
    /// stored verbatim as a city.
    /// </summary>
    internal const int MaxLabelledValueLength = 40;

    /// <summary>
    /// The LABELLED-value rule: split on the FIRST colon; the left side must be a known label
    /// (versioned lexicon vocabulary, lowercased) and the right side must be non-empty and short
    /// enough to be a value rather than a sentence.
    ///
    /// <para><b>The glue is stripped HERE, inside the recogniser — not at the call sites.</b> That is
    /// the whole point, and it was learned the hard way: this rule had two call sites, one of which
    /// trimmed the leading glue and one of which did not. On "- Ort: Göteborg" (an ASCII hyphen is
    /// exactly what a PDF/OCR extractor emits for a sidebar bullet) the subtraction trimmed, read the
    /// label "ort", and CONSUMED the line — while the extractor did not trim, read the label "- ort",
    /// matched nothing, and returned null. The city was claimed by one side and harvested by neither:
    /// it reached NO FIELD AT ALL.
    ///
    /// A rule with two normalisers IS two rules. Normalisation therefore travels WITH the recogniser,
    /// exactly as <see cref="IsPhoneShaped"/> travels with <see cref="Phone"/> — so a call site cannot
    /// forget it, because it never gets the chance to.</para>
    /// </summary>
    internal static bool TryLabelledValue(
        string line, IReadOnlySet<string> labels, out string value)
    {
        value = string.Empty;

        var trimmed = InlineSeparators.TrimGlue(line);
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
            return false;

        var label = trimmed[..colon].Trim().ToLowerInvariant();
        if (!labels.Contains(label))
            return false;

        var candidate = trimmed[(colon + 1)..].Trim();
        if (candidate.Length is 0 or > MaxLabelledValueLength)
            return false;

        value = candidate;
        return true;
    }

    /// <summary>
    /// Is this a BARE kommun ("Göteborg", "• Göteborg", "- Göteborg")? The taxonomy lookup (ADR 0043)
    /// with its normalisation attached, for the same reason as <see cref="TryLabelledValue"/>: the
    /// subtraction and <see cref="ContactLocationExtractor"/>'s rung 3 must ask the question in exactly
    /// the same way, or a city ends up consumed by one and harvested by neither.
    ///
    /// <para>Every call site goes through THIS method. Calling <c>MunicipalityLexicon.IsMunicipality</c>
    /// directly on un-normalised text is how the two sides drifted apart in the first place.</para>
    /// </summary>
    internal static bool IsBareMunicipality(string candidate) =>
        MunicipalityLexicon.IsMunicipality(InlineSeparators.TrimGlue(candidate));

    /// <summary>
    /// Is this whole LINE nothing but a bare kommun? ("Göteborg", "• Göteborg", "Göteborg,",
    /// "· Göteborg ·" — all yes; "Göteborg, Sverige" — no, that is two items.)
    ///
    /// <para><b>It owns its own FRAGMENTATION, and that is the entire point.</b> Sharing the lookup was
    /// not enough: the subtraction derived "this line is one item" by splitting on separators and
    /// counting survivors, while the extractor derived it from the un-split line. **Fragmentation IS a
    /// normaliser** — so the two sides were still asking different questions, and a trailing comma was
    /// enough to prove it:</para>
    ///
    /// <code>
    /// "Göteborg,"  subtraction: split → ["Göteborg", ""] → one survivor → CONSUMES it
    ///              extractor:   no split → "Göteborg," → not a kommun → DECLINES
    ///              ⇒ the city reached NO FIELD AT ALL.
    /// </code>
    ///
    /// <para>That "• Göteborg" worked was a COINCIDENCE — the bullet glyphs happen to sit in both the
    /// glue set and the separator set. Remove the coincidence and the defect is still there. So the
    /// question, the split and the normalisation now live in ONE place, and both call sites pass the
    /// same argument: the raw line.</para>
    /// </summary>
    internal static bool TryBareMunicipalityLine(string line, out string municipality)
    {
        municipality = string.Empty;

        string? single = null;
        foreach (var fragment in InlineSeparators.Split(line))
        {
            var candidate = InlineSeparators.TrimGlue(fragment);
            if (candidate.Length == 0)
                continue;

            // A second item ⇒ this line is not a BARE kommun ("Göteborg, Sverige"). Both sides must
            // decline, and they do, because both ask this method.
            if (single is not null)
                return false;

            single = candidate;
        }

        if (single is null || !MunicipalityLexicon.IsMunicipality(single))
            return false;

        municipality = single;
        return true;
    }
}
