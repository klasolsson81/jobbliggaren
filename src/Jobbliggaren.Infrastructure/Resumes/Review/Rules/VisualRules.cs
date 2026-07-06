using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

// Fas 4b PR-6b — Visual-quality (E) criterion rules. Only E2 (whitespace) is assessed, and
// only from the ONE geometry signal a PDF gives honestly: the tightest page margin. The rest
// of the E family (hierarchy, colour, photo, bullet design, page balance) stays NotAssessed —
// the honest ceiling (ADR 0093 §D4, ADR 0071 OQ3): a margin measurement proves neither type
// hierarchy nor line spacing, so E2 only WARNS on a clearly cramped margin and otherwise reports
// NotAssessed — never a Pass that would over-claim the whole whitespace criterion (parity B5).

/// <summary>
/// E2 Whitespace (High, VisualOnly) — flags a CRAMPED layout from the tightest PDF page margin
/// (Fas 4b PR-6b, ADR 0093 §D4). Warn when the smallest margin is below the floor (~1 cm, the
/// rubric's "marginaler &lt;1 cm" fail signal); otherwise NotAssessed — a healthy margin does
/// not prove the line-spacing / section-spacing half of E2, so the determinism never fabricates
/// a Pass. NotAssessed without geometry (the canonical arm, a DOCX/failed parse, a pre-PR-6b
/// import, or a page whose text could not be located).
/// </summary>
internal sealed class E2WhitespaceRule : ICriterionRule
{
    public string CriterionId => "E2";

    public CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var category = context.Criterion.Category;

        if (context.Layout?.MinMarginPoints is not { } margin)
        {
            return CvCriterionVerdict.NotAssessed("E2", category,
                context.Criterion.NotAssessedReason ?? "Marginalerna kunde inte läsas ur källfilen.");
        }

        // The cramped-margin floor (PDF points) is rubric v2.1 DATA (thresholds), read fail-loud.
        var floor = context.Criterion.RequiredThreshold(RubricThresholdKeys.MinMarginPointsFloor);

        if (margin < floor)
        {
            // No hardcoded cm target in the copy (it would drift from the point-based floor and
            // read like a threshold it is not — architect PR-6b Minor); "wider margins" is the nudge.
            return CvCriterionVerdict.Assessed("E2", category, CriterionVerdict.Warn,
                ReviewText.Cite(ReviewText.Structural(
                    "Marginalerna är knappa. Ge CV:t mer luft med bredare marginaler.")));
        }

        // Margins look adequate, but line spacing and section spacing are not measured from a
        // margin alone — report NotAssessed rather than over-claim the full whitespace criterion.
        // Deliberately a BESPOKE reason, NOT context.Criterion.NotAssessedReason (architect PR-6b
        // Minor): the rubric's data reason ("Vi kan inte läsa det här ur en textbaserad tolkning")
        // would be UNTRUE here — the geometry WAS read; the margin just does not settle the whole
        // criterion. An honest partial-assessment reason; a second rubric data key for one
        // criterion would be over-engineering.
        return CvCriterionVerdict.NotAssessed("E2", category,
            "Marginalerna ser rimliga ut. Radavstånd och luft mellan sektioner bedöms inte i den här versionen.");
    }
}
