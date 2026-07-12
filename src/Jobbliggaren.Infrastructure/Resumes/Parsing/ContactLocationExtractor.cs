using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Extracts the person's city ("Ort") from a CV — #815.
///
/// Before this, <c>ParsedContact</c> was constructed with <c>Location: null</c> hardcoded: city
/// extraction simply did not exist. The consequence was not a near-miss but a systematic one —
/// <c>HasLocation</c> was false for every import ever made, so every parsed-CV review carried a
/// false "ort saknas" and the Slutför guide asked for a city the CV already stated plainly.
///
/// A deterministic priority ladder (NO AI/LLM, ADR 0071). Each rung carries its own evidence, and
/// when none hits the answer is <c>null</c> — honest-absent, never a guess:
///
///   1. <b>Labelled</b> — "Ort: Göteborg", "Bostadsort: …", "Location: …". The label vocabulary is
///      versioned lexicon DATA, never inline C# strings (§5). A label is unambiguous, so this rung
///      may read the whole document.
///   2. <b>Postal-code adjacency</b> — "412 58 Göteborg". A Swedish postnummer followed by a place
///      name. Also unambiguous, also document-wide. This is a SHAPE, so it lives in C# (parity with
///      EmailRegex/DatePatterns), not in the lexicon.
///   3. <b>Bare municipality name</b> — a line that is exactly a known kommun ("Göteborg"), matched
///      against the versioned taxonomy snapshot (ADR 0043). This rung is <b>scoped to the contact
///      block and the preamble ONLY</b>, and that scope is the whole point: "Operatör — Verkstaden
///      AB, Göteborg" states the EMPLOYER's city. Reading it as the person's home would be a
///      fabrication, and this engine never synthesises what the user did not write.
/// </summary>
internal static partial class ContactLocationExtractor
{
    /// <param name="rawText">The whole CV text (rungs 1-2: a label/postal code is unambiguous).</param>
    /// <param name="contactScope">
    /// The contact block plus the preamble — the ONLY place a bare city name may be read from.
    /// </param>
    /// <param name="locationLabels">Lowercased label vocabulary from the versioned lexicon.</param>
    internal static string? Extract(
        string rawText,
        IEnumerable<string> contactScope,
        IReadOnlySet<string> locationLabels)
    {
        return FromLabel(rawText, locationLabels)
            ?? FromPostalCode(rawText)
            ?? FromBareMunicipality(contactScope);
    }

    // Rung 1 — "Ort: Göteborg". Split on the FIRST colon; the left side must be a known label and
    // the right side must be non-empty and short enough to be a place rather than a sentence.
    private static string? FromLabel(string rawText, IReadOnlySet<string> locationLabels)
    {
        foreach (var line in rawText.Split('\n'))
        {
            var trimmed = line.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;

            var label = trimmed[..colon].Trim().ToLowerInvariant();
            if (!locationLabels.Contains(label))
                continue;

            var value = trimmed[(colon + 1)..].Trim();
            if (value.Length is > 0 and <= MaxLocationLength)
                return value;
        }

        return null;
    }

    // Rung 2 — "412 58 Göteborg" / "41258 Göteborg". The place name is whatever follows the code
    // on that line. Capped at MaxLocationLength so a street line cannot smuggle in prose.
    private static string? FromPostalCode(string rawText)
    {
        var match = PostalCodeCityRegex().Match(rawText);
        if (!match.Success)
            return null;

        var city = match.Groups["city"].Value.Trim();
        return city.Length is > 0 and <= MaxLocationLength ? city : null;
    }

    // Rung 3 — a bare kommun name, and ONLY inside contact scope (see the class remarks: an
    // employer's city must never become the person's home).
    private static string? FromBareMunicipality(IEnumerable<string> contactScope)
    {
        foreach (var line in contactScope)
        {
            var trimmed = line.Trim();
            if (MunicipalityLexicon.IsMunicipality(trimmed))
                return trimmed;
        }

        return null;
    }

    /// <summary>
    /// A place name, not a sentence. Sweden's longest municipality name is well under this; the cap
    /// exists so a labelled line carrying prose ("Ort: har bott i Göteborg sedan 2005") cannot be
    /// stored verbatim as a city.
    /// </summary>
    private const int MaxLocationLength = 40;

    // Swedish postnummer: five digits, conventionally written "412 58". The city follows on the
    // same line and may carry Swedish letters, hyphens and spaces ("Upplands Väsby", "Malmö").
    [GeneratedRegex(
        @"\b\d{3}\s?\d{2}\s+(?<city>\p{Lu}[\p{L}\-\s]{1,39})$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex PostalCodeCityRegex();
}
