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
    /// </summary>
    internal static bool TryLabelledValue(
        string line, IReadOnlySet<string> labels, out string value)
    {
        value = string.Empty;

        var trimmed = line.Trim();
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
}
