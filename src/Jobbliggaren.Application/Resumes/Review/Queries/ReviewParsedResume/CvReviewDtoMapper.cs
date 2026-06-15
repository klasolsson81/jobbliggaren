using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

/// <summary>
/// Explicit Application-boundary mapping <see cref="CvReviewResult"/> → <see cref="CvReviewDto"/>
/// (no AutoMapper across the boundary — CLAUDE.md §5). Projects enums to names and the
/// evidence hierarchy to its tagged transport shape.
/// </summary>
internal static class CvReviewDtoMapper
{
    public static CvReviewDto ToDto(this CvReviewResult result) =>
        new(
            RubricVersion: result.RubricVersion.ToString(),
            Profile: result.Profile.ToString(),
            Categories: result.Categories.Select(ToDto).ToList(),
            Verdicts: result.Verdicts.Select(ToDto).ToList(),
            CriticalFails: result.CriticalFails.Select(ToDto).ToList(),
            AssessedCount: result.AssessedCount,
            TotalCount: result.TotalCount);

    private static CvReviewCategoryDto ToDto(CvCategoryResult category) =>
        new(
            Category: category.Category.ToString(),
            PassCount: category.PassCount,
            WarnCount: category.WarnCount,
            FailCount: category.FailCount,
            NotAssessedCount: category.NotAssessedCount,
            Band: category.Band.ToString());

    private static CvCriterionVerdictDto ToDto(CvCriterionVerdict verdict) =>
        new(
            CriterionId: verdict.CriterionId,
            Category: verdict.Category.ToString(),
            Verdict: verdict.Verdict.ToString(),
            Evidence: verdict.Evidence.Select(ToDto).ToList(),
            NotAssessedReason: verdict.NotAssessedReason);

    private static CitedEvidenceDto ToDto(CitedEvidence evidence) => evidence switch
    {
        TextSpanEvidence span => new CitedEvidenceDto(
            Kind: "TextSpan",
            Start: span.Span.Start,
            Length: span.Span.Length,
            Quote: span.Span.Quote,
            Note: span.Note,
            Observation: null),
        StructuralEvidence structural => new CitedEvidenceDto(
            Kind: "Structural",
            Start: null,
            Length: null,
            Quote: null,
            Note: null,
            Observation: structural.Observation),
        _ => throw new ArgumentOutOfRangeException(
            nameof(evidence), evidence.GetType().Name, "Unknown CitedEvidence kind."),
    };
}
