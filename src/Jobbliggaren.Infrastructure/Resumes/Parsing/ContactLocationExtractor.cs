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
///      the e-mail/phone/date patterns), not in the lexicon.
///   3. <b>Bare municipality name</b> — a known kommun ("Göteborg"), matched against the versioned
///      taxonomy snapshot (ADR 0043). This rung is <b>scoped to the contact block and the preamble
///      ONLY</b>, and that scope is the whole point: "Operatör — Verkstaden AB, Göteborg" states the
///      EMPLOYER's city. Reading it as the person's home would be a fabrication, and this engine
///      never synthesises what the user did not write.
///
/// <para><b>The shapes are not owned here</b> (#844). Rungs 1-2 delegate to
/// <see cref="ContactPatterns"/>, which <see cref="HeadingDrivenResumeSegmenter"/> and
/// <see cref="PreambleResidue"/> share. The preamble residue must SUBTRACT precisely what this class
/// RECOGNISES; a second copy of these shapes would let the two drift, which is the 8b.4b Blocker B1
/// defect class.</para>
/// </summary>
internal static class ContactLocationExtractor
{
    /// <param name="rawText">The whole CV text (rungs 1-2: a label/postal code is unambiguous).</param>
    /// <param name="contactScope">
    /// The contact block plus the RAW preamble — the ONLY place a bare city name may be read from.
    /// It must be the RAW preamble, never <see cref="PreambleResidue"/>'s output: the residue
    /// SUBTRACTS the bare-kommun fragment (it is one of its consumption terms), so feeding the
    /// residue here would silently kill rung 3 — the city would be claimed by the subtraction and
    /// harvested by nobody, landing in no field at all.
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

    // Rung 1 — "Ort: Göteborg". The rule (first colon, known label, value short enough to be a place
    // rather than a sentence) lives in ContactPatterns, shared with the residue.
    private static string? FromLabel(string rawText, IReadOnlySet<string> locationLabels)
    {
        foreach (var line in rawText.Split('\n'))
        {
            if (ContactPatterns.TryLabelledValue(line, locationLabels, out var value))
                return value;
        }

        return null;
    }

    // Rung 2 — "412 58 Göteborg" / "41258 Göteborg". The place name is whatever follows the code
    // on that line, capped so a street line cannot smuggle in prose.
    private static string? FromPostalCode(string rawText)
    {
        var match = ContactPatterns.PostalCodeCity().Match(rawText);
        if (!match.Success)
            return null;

        var city = match.Groups["city"].Value.Trim();
        return city.Length is > 0 and <= ContactPatterns.MaxLabelledValueLength ? city : null;
    }

    /// <summary>
    /// Rung 3 — a bare kommun name, inside contact scope only (see the class remarks: an employer's
    /// city must never become the person's home).
    ///
    /// <para><b>Fragment-wise, not whole-line</b> (#844). A sidebar/rail CV linearizes its contact
    /// block onto ONE line — "Anna Andersson | anna@x.se | 070-123 45 67 | Göteborg" — and a
    /// whole-line kommun test never fires on it, so the city was silently lost on the most common
    /// two-column layout (a live defect, alongside the name loss the same line caused).</para>
    ///
    /// <para>The SCOPE is unchanged and remains the honesty guard — contact block + preamble only.
    /// Only the GRANULARITY changes: the fragments of a contact-scope line. An employer's city inside
    /// an experience entry is still out of reach, because that entry is not in contact scope.</para>
    /// </summary>
    private static string? FromBareMunicipality(IEnumerable<string> contactScope)
    {
        foreach (var line in contactScope)
        {
            foreach (var fragment in InlineSeparators.Split(line))
            {
                var candidate = InlineSeparators.TrimGlue(fragment);
                if (MunicipalityLexicon.IsMunicipality(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
