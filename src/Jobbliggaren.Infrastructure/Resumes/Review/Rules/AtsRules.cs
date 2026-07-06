using System.Globalization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4 STEG 9 (F4-9) — ATS-parsability-category (D) criterion rules. Only D1 (file format,
// via the parse-confidence signal) and D6 (standard headings, via the detected sections) are
// assessable from the text parse; D2/D3/D4/D5/D7 (layout geometry), D8 (needs a target ad),
// and D9/D10 (file metadata) have no rule and fall through to the engine's honest NotAssessed.
// Fas 4b PR-4 (ADR 0093 §D8): on the CANONICAL arm both rules verdict on what is known BY
// CONSTRUCTION — app-managed content has no parsed source file, and its sections come from
// the shared linearizer's structure, not heading heuristics (D4-bind parity: app-generated
// CVs report Pass with StructuralEvidence).

/// <summary>D1 Filformat (Critical) — a scanned image-PDF without a text layer is not ATS-parsable.</summary>
internal sealed class D1FileFormatRule : ICriterionRule
{
    public string CriterionId => "D1";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;

        // Canonical arm: there is no source FILE under review — the content is
        // app-managed and its exports are generated text-based by construction
        // (QuestPDF ATS profile). A genuine, evidenced Pass (D4-bind parity), never
        // NotAssessed (the answer is known, hiding it would misreport — OQ3).
        if (context.Source == CvReviewSourceKind.Canonical)
        {
            return CvCriterionVerdict.Assessed("D1", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural(
                    "App-hanterat innehåll: genererade exporter är textbaserade och kan läsas av ett ATS.")));
        }

        var fallback = context.ParseFallback ?? ParseFallbackReason.None;

        return fallback switch
        {
            ParseFallbackReason.ScannedImageNoText => CvCriterionVerdict.Assessed(
                "D1", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural(
                    "Inscannad bild-PDF utan textlager: texten kan inte extraheras av ett ATS."))),

            ParseFallbackReason.ExtractionFailed or ParseFallbackReason.EncodingSuspect =>
                CvCriterionVerdict.Assessed("D1", category, CriterionVerdict.Warn,
                    ReviewText.Cite(ReviewText.Structural(
                        $"Osäker textextraktion ({ReviewEvidenceLabels.Fallback(fallback)})."))),

            _ => CvCriterionVerdict.Assessed("D1", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural(
                    $"Textbaserad fil ({context.SourceContentType}): texten kan extraheras."))),
        };
    }
}

/// <summary>D6 Standardrubriker (High) — recognisable standard section headings were found.</summary>
internal sealed class D6StandardHeadingsRule : ICriterionRule
{
    public string CriterionId => "D6";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;
        var detected = context.DetectedSections;

        if (detected.Count == 0)
        {
            // Staging: no heading heuristic hit (creative headings?). Canonical: the
            // content genuinely carries no standard section — same honest Warn.
            return CvCriterionVerdict.Assessed("D6", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    context.Source == CvReviewSourceKind.Canonical
                        ? "CV:t saknar standardsektioner (erfarenhet, utbildning, kompetenser)."
                        : "Inga standardsektioner kunde identifieras via rubriker (kreativa rubriker?).")));
        }

        var labels = detected.Select(ReviewEvidenceLabels.Section);
        return CvCriterionVerdict.Assessed("D6", category, CriterionVerdict.Pass,
            ReviewText.Cite(ReviewText.Structural(
                context.Source == CvReviewSourceKind.Canonical
                    ? $"Standardsektioner finns i CV-strukturen: {string.Join(", ", labels)}."
                    : $"Standardsektioner identifierade via rubriker: {string.Join(", ", labels)}.")));
    }
}

/// <summary>
/// D9 Filstorlek (Low, AtsOnly) — the imported file size (Fas 4b PR-6b, ADR 0093 §D4).
/// NotAssessed without metrics (the canonical arm or a pre-PR-6b import). File size is
/// format-agnostic, so this assesses a DOCX import too (the analyzer reports the size even when
/// geometry is NotApplicable). Fail above the hard ceiling, Warn above the soft ceiling, else
/// Pass — both bounds are rubric v2.1 DATA (thresholds), read fail-loud.
/// </summary>
internal sealed class D9FileSizeRule : ICriterionRule
{
    public string CriterionId => "D9";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;

        if (context.Layout is not { } layout)
        {
            return CvCriterionVerdict.NotAssessed("D9", category,
                context.Criterion.NotAssessedReason ?? "Filstorleken kunde inte läsas ur källfilen.");
        }

        var warnBytes = context.Criterion.RequiredThreshold(RubricThresholdKeys.FileSizeWarnBytes);
        var failBytes = context.Criterion.RequiredThreshold(RubricThresholdKeys.FileSizeFailBytes);
        var bytes = layout.FileSizeBytes;

        // Decimal COMMA in UI copy (CLAUDE.md §10) — format invariant (deterministic, no current-
        // culture dependence, §5) then swap the point for a comma.
        var megabytes = (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture)
            .Replace('.', ',');

        // The copy deliberately does NOT echo the threshold value (the Fail/Warn/Pass split is
        // data-driven above): a hardcoded "under 2 MB" would drift from the rubric threshold AND
        // read as self-contradictory against the rounded size right at the boundary (architect +
        // code-reviewer PR-6b Minor). The measured size + "compress the images" is the honest nudge.
        if (bytes > failBytes)
        {
            return CvCriterionVerdict.Assessed("D9", category, CriterionVerdict.Fail,
                ReviewText.Cite(ReviewText.Structural(
                    $"Filen är {megabytes} MB och onödigt stor. Komprimera bilderna.")));
        }

        return bytes > warnBytes
            ? CvCriterionVerdict.Assessed("D9", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    $"Filen är {megabytes} MB. Komprimera gärna bilderna för en lättare fil.")))
            : CvCriterionVerdict.Assessed("D9", category, CriterionVerdict.Pass,
                ReviewText.Cite(ReviewText.Structural($"Filstorleken är {megabytes} MB.")));
    }
}
