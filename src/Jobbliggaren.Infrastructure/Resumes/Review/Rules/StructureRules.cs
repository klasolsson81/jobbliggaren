using System.Text.RegularExpressions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4 STEG 9 (F4-9) — Structure-category (B) criterion rules. B2 (length, needs page
// count) and B5 (formatting, needs fonts) have no rule — they fall through to the engine's
// honest NotAssessed (layout signal the text parse cannot see).

/// <summary>B1 Sektioner och ordning (High) — the core sections are present.</summary>
internal sealed class B1SectionsRule : ICriterionRule
{
    public string CriterionId => "B1";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var outcome = context.Resume.Personnummer;

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

/// <summary>B6 Datumformat konsekvent (High) — conditional on parseable periods (V-C).</summary>
internal sealed class B6DateFormatRule : ICriterionRule
{
    public string CriterionId => "B6";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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

        return formats.Count == 1
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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
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

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var fileName = context.Resume.SourceFileName ?? string.Empty;

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
