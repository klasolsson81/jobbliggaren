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
    /// The shortest and longest number of whitespace-separated tokens a recognised person name may
    /// carry, and its length cap (60 — the cap the old heuristic already used, kept).
    ///
    /// <para>FORM, so it lives in C# and not in the lexicon, exactly like
    /// <see cref="MinPhoneDigits"/> and <see cref="MaxLabelledValueLength"/>. These are emphatically
    /// NOT rubric thresholds: the rubric governs review VERDICTS, never what the parser recognises
    /// (ADR 0107 §3 owns parsing; ADR 0108's kind-boundary is about the knowledge bank).</para>
    /// </summary>
    private const int MinNameTokens = 2;
    private const int MaxNameTokens = 4;
    internal const int MaxNameLength = 60;

    /// <summary>
    /// Is this line a person's NAME? (#898.)
    ///
    /// <para><b>What it replaces.</b> The segmenter used to ask <c>IsNameLike</c> — "the first
    /// substantial line under 60 characters that is not an e-mail, a phone or a date" — and used the
    /// answer as if it were a recognised name. It is a heuristic that ALWAYS answers, so on the very
    /// common layout that puts the job title above the name it returned <c>"Systemutvecklare"</c>, and
    /// on a CV whose summary sits above a "Kontakt" heading it returned half that summary. Prose in a
    /// field labelled <i>namn</i>, which B3 then verdicts on.</para>
    ///
    /// <para><b>The rule.</b> The line must carry exactly ONE item (see the fragmentation note below),
    /// and over that glue-trimmed item: no digit, no colon, no e-mail, 2–4 whitespace tokens, and every
    /// token either starts with an uppercase letter or is a known <c>nameParticles</c> member (with at
    /// least one of the former, so a line of only particles is not a name). A token's TAIL is
    /// deliberately unconstrained: "ANNA ANDERSSON" is a very common CV header and must pass.</para>
    ///
    /// <para><b>It owns its FRAGMENTATION, and that is not a detail</b> — it is the same lesson
    /// <see cref="TryBareMunicipalityLine"/> was rewritten for: <b>fragmentation IS a normaliser</b>.
    /// Without it this method had two call sites asking different questions. The preamble arm passes
    /// residue FRAGMENTS, so "Anna Andersson, Undersköterska" arrives split and resolves to the name;
    /// the Kontakt-block arm passes RAW LINES, so the same text resolved to
    /// <c>"Anna Andersson, Undersköterska"</c> — a job title in a field labelled <i>namn</i>, i.e. the
    /// very defect #898 exists to remove, surviving in the half of the parser nobody re-read.
    /// ("Göteborg, Sverige" was a name to that arm too.) Both call sites now pass the raw line and get
    /// the same answer, and the preamble arm is unaffected because splitting an already-split fragment
    /// is idempotent.</para>
    ///
    /// <para><b>Refusal means <c>false</c>, never a fallback.</b> A recogniser that sometimes declines
    /// beats a heuristic that always answers, because a guess is indistinguishable from knowledge at
    /// the point of use.</para>
    ///
    /// <para><b>What it declines, in full — the honest list.</b> Accepted false negatives: a mononym
    /// ("Zlatan"); a 5+ token name; a lowercase-styled name or one whose first token is lowercase and
    /// not a particle ("d'Angelo"); a labelled "Namn: Anna Andersson" line; a name line carrying a
    /// digit ("Anna Andersson 1985", a birth year on the header line); and a name glued to a second
    /// item on the same line ("Andersson, Anna", "Anna Andersson | anna@x.se") outside the preamble,
    /// where the residue does the splitting. Each yields no name, the gap is surfaced honestly
    /// (<c>ParsedGapSummary.HasFullName</c>, <c>ContactConfidence</c>, B3), and the user fills it in —
    /// propose-and-approve (ADR 0040), never invent.</para>
    ///
    /// <para><b>And the false POSITIVE class that remains</b>, said out loud rather than left for a
    /// reader to trust the method name: a 2–4 token title-cased line that is not a name still passes
    /// ("Front End Developer", "CV Anna Andersson"). No deterministic shape rule separates those from
    /// "Anna Andersson", and inventing a job-title lexicon would be a different change with a different
    /// risk profile. Swedish sentence casing keeps the class small ("Senior systemutvecklare" is
    /// refused on its lowercase second token), and it is strictly smaller than the heuristic's, which
    /// accepted any line at all.</para>
    ///
    /// <para><b>The phone and date arms of the old heuristic are absent on purpose</b>, not forgotten:
    /// both shapes require digits, which the digit rule already refuses, so re-asking would be a guard
    /// that cannot change an outcome — and a test pinning one would pass for the wrong reason. The
    /// e-mail arm IS load-bearing (an address need carry no digit) and delegates to
    /// <see cref="Email"/>, never a second copy (ADR 0107 §3).</para>
    ///
    /// <para><b>The normalisation lives HERE, inside the recogniser</b> — the #844 lesson, applied to
    /// the name question. That is what makes a bulleted contact block ("• Anna Andersson") yield
    /// <c>Anna Andersson</c> rather than the bullet with it, at every call site, without any call site
    /// having to remember. Output is a pure trim of the user's own text: tokenisation drives the
    /// DECISION only, never the VALUE (no internal whitespace collapse — see the display-form
    /// invariants in <see cref="CvParsingLexiconLoader"/> for why re-writing user text is off limits).</para>
    /// </summary>
    /// <param name="particles">Versioned lexicon vocabulary (<c>nameParticles</c>), lowercased —
    /// passed in, because this class owns FORM and never vocabulary (see the class remarks).</param>
    internal static bool TryPersonName(string line, IReadOnlySet<string> particles, out string name)
    {
        ArgumentNullException.ThrowIfNull(particles);

        name = string.Empty;

        // The line must be ONE item. A second surviving fragment means the line glues the name to
        // something else ("Andersson, Anna", "Anna Andersson | anna@x.se", "Göteborg, Sverige"), and a
        // recogniser that answered from the first fragment would be doing the caller's splitting with
        // the caller's luck — which is exactly how the preamble arm and the Kontakt-block arm came to
        // disagree about the same text. Identical shape to TryBareMunicipalityLine, for the identical
        // reason. Idempotent for the preamble arm, which already passes single fragments.
        string? candidate = null;
        foreach (var fragment in InlineSeparators.Split(line))
        {
            var item = InlineSeparators.TrimGlue(fragment);
            if (item.Length == 0)
                continue;

            if (candidate is not null)
                return false;

            candidate = item;
        }

        if (candidate is null || candidate.Length > MaxNameLength)
            return false;

        foreach (var c in candidate)
        {
            // A name carries no digit (that also disposes of every phone and every date range), and a
            // colon makes the line a LABEL, not a bare name.
            if (char.IsAsciiDigit(c) || c == ':')
                return false;
        }

        if (Email().IsMatch(candidate))
            return false;

        var tokens = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < MinNameTokens or > MaxNameTokens)
            return false;

        var carriesCapitalisedToken = false;
        foreach (var token in tokens)
        {
            if (char.IsUpper(token[0]))
            {
                carriesCapitalisedToken = true;
                continue;
            }

            if (!particles.Contains(token.ToLowerInvariant()))
                return false;
        }

        if (!carriesCapitalisedToken)
            return false;

        name = candidate;
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
