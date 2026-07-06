using System.Text.RegularExpressions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4 STEG 9 (F4-9) — Structure-category (B) criterion rules. B2 (length, needs page
// count) has no rule — it falls through to the engine's honest NotAssessed (the page-count
// signal the text parse cannot see; PR-6b's ICvLayoutAnalyzer feeds it). B5 (formatting) is
// assessed GEOMETRY-FREE from Fas 4b PR-6 (mixed bullet markers in the linearized text) —
// Warn or NotAssessed, never Pass (the font/heading half still needs PDF geometry, deferred).

/// <summary>B1 Sektioner och ordning (High) — the core sections are present.</summary>
internal sealed class B1SectionsRule : ICriterionRule
{
    public string CriterionId => "B1";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var content = context.Content;

        var hasExperience = content.Experience.Count > 0;
        var hasEducation = content.Education.Count > 0;
        var hasContact = content.Contact is not null
            && (!string.IsNullOrWhiteSpace(content.Contact.FullName)
                || !string.IsNullOrWhiteSpace(content.Contact.Email));

        var missing = new List<string>();
        if (!hasContact)
        {
            missing.Add("kontakt");
        }

        if (!hasExperience)
        {
            missing.Add("arbetslivserfarenhet");
        }

        if (!hasEducation)
        {
            missing.Add("utbildning");
        }

        // Fail-signal: "Saknar erfarenhet/utbildning".
        if (!hasExperience || !hasEducation)
        {
            return CvCriterionVerdict.Assessed("B1", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural($"Saknar kärnsektion(er): {string.Join(", ", missing)}.")));
        }

        return missing.Count > 0
            ? CvCriterionVerdict.Assessed("B1", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural($"Saknar sektion(er): {string.Join(", ", missing)}.")))
            : CvCriterionVerdict.Assessed("B1", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Kontakt-, erfarenhets- och utbildningssektion finns.")));
    }
}

/// <summary>B3 Kontaktuppgifter kompletta (Critical) — email + phone present; structural evidence.</summary>
internal sealed class B3ContactRule : ICriterionRule
{
    public string CriterionId => "B3";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var contact = context.Content.Contact;

        var hardMissing = new List<string>();
        if (contact is null || string.IsNullOrWhiteSpace(contact.Email))
        {
            hardMissing.Add("e-post");
        }

        if (contact is null || string.IsNullOrWhiteSpace(contact.Phone))
        {
            hardMissing.Add("telefon");
        }

        // Fail-signal: "Saknar e-post/telefon".
        if (hardMissing.Count > 0)
        {
            return CvCriterionVerdict.Assessed("B3", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural(
                    $"Kontaktsektion hittad; saknar {string.Join(" och ", hardMissing)}.")));
        }

        var softMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(contact!.FullName))
        {
            softMissing.Add("namn");
        }

        if (string.IsNullOrWhiteSpace(contact.Location))
        {
            softMissing.Add("ort");
        }

        return softMissing.Count > 0
            ? CvCriterionVerdict.Assessed("B3", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural($"Kontaktsektion hittad; saknar {string.Join(", ", softMissing)}.")))
            : CvCriterionVerdict.Assessed("B3", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Namn, e-post, telefon och ort finns i klartext.")));
    }
}

/// <summary>B4 Personnummer ej angivet (Critical) — reads the PII-safe scan outcome (Inv.1).</summary>
internal sealed class B4PersonnummerRule : ICriterionRule
{
    public string CriterionId => "B4";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var outcome = context.Personnummer;

        // Canonical arm (Fas 4b PR-4, CTO-bind Q6): the canonical Resume is
        // personnummer-guarded BY CONSTRUCTION — ResumeContentPersonnummerGuard runs on
        // every write path (promote gap-fill + master edits) and EnsureReadyForPromotion
        // blocks a flagged import. B4's clean verdict is a KNOWN result here, evidenced
        // structurally (OQ3 cuts both ways: never fabricate a pass, never hide a known
        // one). The adapter supplies the clean outcome; this branch carries the honest
        // provenance of that knowledge in its citation.
        if (context.Source == CvReviewSourceKind.Canonical)
        {
            return CvCriterionVerdict.Assessed("B4", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural(
                    "Inget personnummer i CV-texten. Innehållet kontrolleras vid varje sparning.")));
        }

        // Auto-fail when a personnummer is present IN THE CV BODY — cite only the count/
        // structure, NEVER the raw value or offsets (ADR 0074 Invariant 1; the outcome is
        // PII-safe by construction). #426: if the FILENAME also carries one, append the rename
        // prompt to the same observation (the body fail dominates; the filename note rides
        // along). Copy is em-dash-free per the design-copy skill (rendered Swedish text).
        if (outcome.Found)
        {
            var note = outcome.FoundInFileName
                ? $"Personnummer hittat ({outcome.Count} förekomst(er)) i CV-texten. Ta bort före användning (IMY-rekommendation). Filnamnet innehåller också ett personnummer. Byt filnamn."
                : $"Personnummer hittat ({outcome.Count} förekomst(er)). Ta bort före användning (IMY-rekommendation).";
            return CvCriterionVerdict.Assessed("B4", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural(note)));
        }

        // #426 (defense-in-depth): the body is clean but the FILENAME carries a personnummer.
        // A Warn, not a Fail — a filename personnummer never reaches the canonical Resume, so it
        // must not block promotion (ParsedResume.EnsureReadyForPromotion reads the body signal
        // only); it prompts the user to rename the file.
        if (outcome.FoundInFileName)
        {
            return CvCriterionVerdict.Assessed("B4", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    "Personnummer i filnamnet. Byt filnamn före användning (IMY-rekommendation). Inget personnummer i själva CV-texten.")));
        }

        return CvCriterionVerdict.Assessed("B4", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural("Inget personnummer hittat i CV-texten.")));
    }
}

/// <summary>
/// B5 Konsekvent formatering (High, styleOnly) — GEOMETRY-FREE detection of mixed bullet
/// markers across the experience descriptions (Fas 4b PR-6, ADR 0093 §D4, CTO-bind D-G).
/// Warn when 2+ distinct lead markers are used; otherwise NotAssessed — NEVER Pass, since a
/// clean marker set does not prove the fonts/heading-levels B5 also covers were checked
/// (that needs PDF geometry the text parse cannot see; the honest ceiling, ADR 0071 OQ3).
/// The single text-derived signal keeps the verdict ARM-INDEPENDENT: the canonical
/// <c>SetFindingStatus</c> recompute yields the SAME verdict as the review the user saw, so
/// the styleOnly "Ignored" decision is genuinely reachable end-to-end — which is what closes
/// PR-5's intentional fail-closed gap (no styleOnly criterion had an assessable rule before).
/// </summary>
internal sealed class B5ConsistentFormattingRule : ICriterionRule
{
    public string CriterionId => "B5";

    // The lead glyphs that mark a bullet/list item at the START of a trimmed description
    // line. A detection-shape set (code, ADR 0093 §D3) — NOT a rubric threshold: two
    // different bullet glyphs is inconsistency BY DEFINITION, not a tunable policy. Includes
    // the ASCII hyphen/asterisk bullets and the common typographic bullet glyphs.
    private static readonly HashSet<char> BulletMarkers =
    [
        '•', // • bullet
        '◦', // ◦ white bullet
        '‣', // ‣ triangular bullet
        '·', // · middle dot
        '●', // ● black circle
        '○', // ○ white circle
        '▪', // ▪ black small square
        '▫', // ▫ white small square
        '■', // ■ black square
        '–', // – en dash
        '—', // — em dash
        '-',      // hyphen-minus (the common ASCII bullet)
        '*',      // asterisk
    ];

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;

        // SortedSet → distinct + deterministic order (the count is what drives the verdict;
        // the glyphs themselves are never echoed into the user-facing evidence).
        var markers = new SortedSet<char>();
        foreach (var experience in context.Content.Experience)
        {
            foreach (var line in ReviewText.DescriptionLines(experience))
            {
                if (LeadMarker(line) is { } marker)
                {
                    markers.Add(marker);
                }
            }
        }

        if (markers.Count >= 2)
        {
            // Describe the problem WITHOUT echoing the raw glyphs (an em/en-dash echoed into
            // user copy would trip the civic no-em-dash rule; the user knows their own bullets).
            return CvCriterionVerdict.Assessed("B5", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    "Blandade punktsymboler i beskrivningarna. Välj en enhetlig punktstil.")));
        }

        // Never Pass: a single/no marker does not prove the full B5 (typsnitt, rubriknivåer)
        // is consistent — that needs PDF geometry (deferred, honest ceiling). Reason is
        // versioned rubric DATA (ADR 0071 reasons-as-data); N-1 fallback stays civic.
        return CvCriterionVerdict.NotAssessed("B5", category,
            context.Criterion.NotAssessedReason
            ?? "Formateringens konsekvens bedöms inte i den här versionen.");
    }

    // The leading bullet glyph of a trimmed line, or null when the line does not start with a
    // known marker followed by whitespace (so a marker glyph glued to a word — a rare
    // false-positive like "*viktigt*" markdown emphasis mid-list — is not counted as a bullet;
    // a real bullet is "marker + space + text").
    private static char? LeadMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 2 || !BulletMarkers.Contains(trimmed[0]) || !char.IsWhiteSpace(trimmed[1]))
        {
            return null;
        }

        // A marker (esp. the ambiguous "-") leading a BARE date range ("- 2020 – nuvarande") is
        // a period line, not a bullet STYLE — excluding it avoids a spurious mixed-marker Warn
        // (architect PR-6a Minor). A date-only line demonstrates no punctuation style, so
        // dropping it costs nothing. Reuses the SAME parser DescriptionLines filters full
        // period lines with (#487).
        var rest = trimmed[1..].TrimStart();
        if (PeriodParser.TryParse(rest, out _, out _, out _))
        {
            return null;
        }

        return trimmed[0];
    }
}

/// <summary>B6 Datumformat konsekvent (High) — conditional on parseable periods (V-C).</summary>
internal sealed class B6DateFormatRule : ICriterionRule
{
    public string CriterionId => "B6";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var formats = context.DatedExperiences
            .Where(d => d.Parsed)
            .Select(d => d.FormatToken!)
            .Distinct()
            .ToList();

        if (formats.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "B6", category, "Perioderna kunde inte tolkas. Datumformat-konsekvens bedöms ej v1.");
        }

        // The distinct-format ceiling is rubric v1.2 DATA (thresholds.maxDistinctDateFormats),
        // read fail-loud — never a C# literal.
        return formats.Count <= context.Criterion.RequiredThreshold(RubricThresholdKeys.MaxDistinctDateFormats)
            ? CvCriterionVerdict.Assessed("B6", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural($"Konsekvent datumformat ({formats[0]}) genomgående.")))
            : CvCriterionVerdict.Assessed("B6", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural($"Blandade datumformat: {string.Join(", ", formats)}.")));
    }
}

/// <summary>B7 Kronologi tydlig (High) — conditional on parseable periods; reverse-chronological.</summary>
internal sealed class B7ChronologyRule : ICriterionRule
{
    public string CriterionId => "B7";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var dated = context.DatedExperiences.Where(d => d.Parsed).ToList();

        if (dated.Count == 0)
        {
            return CvCriterionVerdict.NotAssessed(
                "B7", category, "Perioderna kunde inte tolkas. Kronologi bedöms ej v1.");
        }

        if (dated.Count == 1)
        {
            return CvCriterionVerdict.Assessed("B7", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("En daterad roll; kronologi trivialt korrekt.")));
        }

        // Reverse-chronological = the entries, as listed, have non-increasing start dates.
        var starts = dated.Select(d => d.Start!.Value).ToList();
        var reverseChronological = starts.Zip(starts.Skip(1), (a, b) => a >= b).All(ordered => ordered);

        return reverseChronological
            ? CvCriterionVerdict.Assessed("B7", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural("Rollerna är listade omvänt kronologiskt (senaste först).")))
            : CvCriterionVerdict.Assessed("B7", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural("Rollerna är inte konsekvent omvänt kronologiska.")));
    }
}

/// <summary>B8 Filnamn (Low) — Arbetsförmedlingen's CV_Förnamn_Efternamn recommendation.</summary>
internal sealed partial class B8FileNameRule : ICriterionRule
{
    public string CriterionId => "B8";

    [GeneratedRegex(@"^CV[_\- ]\p{L}+([_\- ]\p{L}+)+\.(pdf|docx)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RecommendedRegex();

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        // Canonical CVs carry no filename until PR-9's Form C stores the original file —
        // the empty-name branch below reports the honest NotAssessed (CTO-bind Q6).
        var fileName = context.SourceFileName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return CvCriterionVerdict.NotAssessed(
                "B8", category, "Källfilens namn saknas. Filnamnsrekommendationen bedöms ej.");
        }

        if (RecommendedRegex().IsMatch(fileName))
        {
            return CvCriterionVerdict.Assessed("B8", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural($"Filnamnet följer rekommendationen: {fileName}.")));
        }

        var lower = fileName.ToLowerInvariant();
        var generic = lower is "cv.pdf" or "cv.docx"
            || lower.StartsWith("document", StringComparison.Ordinal)
            || lower.StartsWith("dokument", StringComparison.Ordinal)
            || lower.StartsWith("untitled", StringComparison.Ordinal);

        var note = generic
            ? $"Generiskt filnamn: {fileName}. Rekommendation: CV_Förnamn_Efternamn.pdf."
            : $"Filnamnet följer inte rekommendationen CV_Förnamn_Efternamn: {fileName}.";
        return CvCriterionVerdict.Assessed("B8", category, CriterionVerdict.Warn,
            ReviewText.Cite(ReviewText.Structural(note)));
    }
}
