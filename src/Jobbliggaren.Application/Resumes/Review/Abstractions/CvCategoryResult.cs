using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The per-category outcome of a CV review (Fas 4 STEG 9, F4-9). Category-PRIMARY by
/// design (BUILD §8.1, Goodhart guard): the verdict COUNTS are the headline signal;
/// the <paramref name="Band"/> is the secondary, data-derived label (mapped onto the
/// rubric's bands from the weighted PASS-fraction over the ASSESSED criteria only —
/// NotAssessed criteria are excluded from the denominator, so the engine never
/// penalises what it cannot assess). No opaque numeric score is exposed.
/// </summary>
public sealed record CvCategoryResult(
    RubricCategory Category,
    int PassCount,
    int WarnCount,
    int FailCount,
    int NotAssessedCount,
    ScoreBandLabel Band,
    IReadOnlyList<CvCriterionVerdict> Verdicts);
