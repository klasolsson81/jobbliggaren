using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The per-category outcome of a CV review (Fas 4 STEG 9, F4-9). Category-PRIMARY by
/// design (BUILD §8.1, Goodhart guard): the verdict COUNTS are the headline signal;
/// the <paramref name="Band"/> is the secondary, data-derived label (mapped onto the
/// rubric's bands from the weighted PASS-fraction over the ASSESSED criteria only —
/// NotAssessed criteria are excluded from the denominator, so the engine never
/// penalises what it cannot assess). No opaque numeric score is exposed.
/// <para>
/// <paramref name="Band"/> is <c>null</c> when the category has NO assessed criterion —
/// an UNBANDED state, not a low one (#1062 B1). The denominator is then empty, so every
/// band the rubric offers would be a claim about a measurement that was never taken; the
/// floor band in particular reads as the lowest grade, which is exactly the
/// "NotAssessed rendered as a low grade" that CLAUDE.md §5 forbids. Nullable REMOVES a
/// claim — a fifth <see cref="ScoreBandLabel"/> member was rejected because the four
/// members correspond 1:1 with the rubric asset's <c>bands[]</c> tokens and a new label
/// would add vocabulary the data can never supply (CTO-bind 2026-08-17 §1).
/// </para>
/// </summary>
public sealed record CvCategoryResult(
    RubricCategory Category,
    int PassCount,
    int WarnCount,
    int FailCount,
    int NotAssessedCount,
    ScoreBandLabel? Band,
    IReadOnlyList<CvCriterionVerdict> Verdicts);
