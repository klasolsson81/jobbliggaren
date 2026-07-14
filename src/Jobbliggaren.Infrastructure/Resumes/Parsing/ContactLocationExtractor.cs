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

    /// <summary>
    /// Rung 1 — "Ort: Göteborg". The rule (first colon, known label, value short enough to be a place
    /// rather than a sentence) lives in <see cref="ContactPatterns"/>, shared with the residue.
    ///
    /// <para><b>Read FRAGMENT-wise, because the residue subtracts fragment-wise.</b> Sharing the
    /// PATTERN does not share the evaluation SCOPE, and the scope is where the two drift apart. A rail
    /// line puts several items on one line, and a line-wise label read is wrong on it in BOTH
    /// directions: "Ort: Göteborg | anna@x.se" split on the line's first colon yields the value
    /// "Göteborg | anna@x.se" — the e-mail lands IN the Ort field (a live defect, pre-#844) — while
    /// "Anna | Ort: Göteborg | mail" yields the label "anna | ort", which matches nothing, so the city
    /// is CONSUMED BY THE SUBTRACTION AND HARVESTED BY NOBODY.
    ///
    /// Fragment-wise, both read correctly, and the extractor claims exactly what the subtraction
    /// removes. That agreement IS the contract — it is not an optimisation.</para>
    /// </summary>
    private static string? FromLabel(string rawText, IReadOnlySet<string> locationLabels)
    {
        foreach (var line in rawText.Split('\n'))
        {
            foreach (var fragment in InlineSeparators.Split(line))
            {
                if (ContactPatterns.TryLabelledValue(fragment, locationLabels, out var value))
                    return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Rung 2 — "412 58 Göteborg" / "41258 Göteborg". The place name is whatever follows the code,
    /// capped so a street line cannot smuggle in prose.
    ///
    /// <para><b>Also fragment-wise, and here it is load-bearing rather than merely tidy.</b>
    /// <see cref="ContactPatterns.PostalCodeCity"/> is <c>$</c>-anchored: on
    /// "Anna Andersson | 412 58 Göteborg | anna@x.se" the city group cannot cross the "|", so a
    /// whole-line read never matches — while the residue, which evaluates per FRAGMENT, matches and
    /// CONSUMES it. The address would then exist in NO FIELD AT ALL, and whether it worked would depend
    /// on where the user happened to put the city on her rail. Per fragment, the anchor binds to the
    /// fragment's end and the two agree.</para>
    /// </summary>
    private static string? FromPostalCode(string rawText)
    {
        foreach (var line in rawText.Split('\n'))
        {
            foreach (var fragment in InlineSeparators.Split(line))
            {
                var match = ContactPatterns.PostalCodeCity().Match(fragment);
                if (!match.Success)
                    continue;

                var city = match.Groups["city"].Value.Trim();
                if (city.Length is > 0 and <= ContactPatterns.MaxLabelledValueLength)
                    return city;
            }
        }

        return null;
    }

    /// <summary>
    /// Rung 3 — a bare kommun name, inside contact scope only (see the class remarks: an employer's
    /// city must never become the person's home).
    ///
    /// <para><b>Fragment-wise, but ONLY on a line that carries a contact span</b> (#844). A
    /// sidebar/rail CV linearizes its contact block onto ONE line — "Anna Andersson | anna@x.se |
    /// 070-123 45 67 | Göteborg" — and a whole-line kommun test never fires on it, so the city was
    /// silently lost on the most common two-column layout (a live defect, alongside the name loss the
    /// same line caused).
    ///
    /// <b>The contact-span condition is a correctness gate, not an optimisation.</b> Contact scope is
    /// "the preamble + the Contact block" — but when a CV uses headings the lexicon does not know, ZERO
    /// headings are detected and <c>PreambleLines</c> returns the WHOLE DOCUMENT. Splitting every line
    /// of it would read "Undersköterska — Vårdcentralen, Göteborg" and make the EMPLOYER's city the
    /// person's home — the exact fabrication this class was written to refuse (ADR 0071), and one that
    /// would silently reach every CV with a personal heading vocabulary. Requiring an e-mail / phone /
    /// postal-code span on the line means we only fragment a line that has ALREADY identified itself
    /// as a contact rail. An experience entry never has.</para>
    ///
    /// <para>A line with NO contact span keeps the original whole-line rule — so a plain "Göteborg" on
    /// its own line in the contact block still resolves, exactly as before.</para>
    /// </summary>
    private static string? FromBareMunicipality(IEnumerable<string> contactScope)
    {
        foreach (var line in contactScope)
        {
            var whole = line.Trim();

            if (!PreambleResidue.CarriesContactSpan(whole))
            {
                // No contact span ⇒ this is not a rail. Whole-line rule only (pre-#844 behaviour) —
                // but TRIM THE GLUE FIRST, exactly as the subtraction does. A bulleted contact line
                // ("• Göteborg") is TrimGlue'd before IsMunicipality on the subtraction side and was
                // NOT here, so the kommun was consumed by the subtraction and harvested by nobody:
                // the city reached no field at all. The two sides must normalise identically, or the
                // agreement is a claim rather than a fact.
                var bare = InlineSeparators.TrimGlue(whole);
                if (MunicipalityLexicon.IsMunicipality(bare))
                    return bare;

                continue;
            }

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
