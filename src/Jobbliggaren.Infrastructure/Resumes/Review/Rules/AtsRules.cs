using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4 STEG 9 (F4-9) — ATS-parsability-category (D) criterion rules. Only D1 (file format,
// via the parse-confidence signal) and D6 (standard headings, via the detected sections) are
// assessable from the text parse; D2/D3/D4/D5/D7 (layout geometry), D8 (needs a target ad),
// and D9/D10 (file metadata) have no rule and fall through to the engine's honest NotAssessed.

/// <summary>D1 Filformat (Critical) — a scanned image-PDF without a text layer is not ATS-parsable.</summary>
internal sealed class D1FileFormatRule : ICriterionRule
{
    public string CriterionId => "D1";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var fallback = context.Resume.Confidence.Fallback;

        return fallback switch
        {
            ParseFallbackReason.ScannedImageNoText => CvCriterionVerdict.Assessed(
                "D1", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural(
                    "Inscannad bild-PDF utan textlager — texten kan inte extraheras av ett ATS."))),

            ParseFallbackReason.ExtractionFailed or ParseFallbackReason.EncodingSuspect =>
                CvCriterionVerdict.Assessed("D1", category, CriterionVerdict.Warn,
                    ReviewText.Cite(ReviewText.Structural(
                        $"Osäker textextraktion ({ReviewEvidenceLabels.Fallback(fallback)})."))),

            _ => CvCriterionVerdict.Assessed("D1", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural(
                    $"Textbaserad fil ({context.Resume.SourceContentType}) — texten kan extraheras."))),
        };
    }
}

/// <summary>D6 Standardrubriker (High) — recognisable standard section headings were found.</summary>
internal sealed class D6StandardHeadingsRule : ICriterionRule
{
    public string CriterionId => "D6";

    public CvCriterionVerdict Evaluate(CvReviewContext context)
    {
        var category = context.Criterion.Category;
        var detected = context.Resume.Confidence.Sections
            .Where(s => s.Level != SectionConfidenceLevel.NotFound)
            .Select(s => s.Kind)
            .Distinct()
            .ToList();

        if (detected.Count == 0)
        {
            return CvCriterionVerdict.Assessed("D6", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    "Inga standardsektioner kunde identifieras via rubriker (kreativa rubriker?).")));
        }

        var labels = detected.Select(ReviewEvidenceLabels.Section);
        return CvCriterionVerdict.Assessed("D6", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural(
                $"Standardsektioner identifierade via rubriker: {string.Join(", ", labels)}.")));
    }
}
