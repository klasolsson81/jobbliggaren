using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

/// <summary>
/// One deterministic rule for one rubric criterion (Fas 4 STEG 9, F4-9). The rule LOGIC
/// is code; every threshold/list/weight it consults lives as F4-7 knowledge-bank DATA
/// (CLAUDE.md §5). <see cref="CvReviewEngine"/> dispatches to the rule keyed by
/// <see cref="CriterionId"/>; a criterion with no rule (or pinned NotAssessedV1) falls
/// through to <see cref="NotAssessedRule"/>.
/// </summary>
internal interface ICriterionRule
{
    /// <summary>The rubric criterion id this rule evaluates (e.g. "A1").</summary>
    string CriterionId { get; }

    /// <summary>Produces the verdict + cited evidence for this criterion (Invariant 2).</summary>
    CvCriterionVerdict Evaluate(CvReviewContext context);
}
