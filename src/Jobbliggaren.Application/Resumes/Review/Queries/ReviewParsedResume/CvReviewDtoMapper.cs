using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

/// <summary>
/// Explicit Application-boundary mapping <see cref="CvReviewResult"/> → <see cref="CvReviewDto"/>
/// (no AutoMapper across the boundary — CLAUDE.md §5). Projects enums to names and the
/// evidence hierarchy to its tagged transport shape.
/// </summary>
internal static class CvReviewDtoMapper
{
    // The human criterion headings live on the rubric (RubricCriterion.Name), not on the
    // verdict (which is the assessment OUTCOME). The handler supplies the criterionId→Name
    // lookup from the rubric so the DTO carries the readable heading from its single source
    // of truth — never a duplicated FE-side code→title map (CTO Decision, CLAUDE.md §10/§5).
    // statusOverlay (Fas 4b PR-4, D2(e)): criterionId → the surviving user decision, supplied
    // only by the canonical review handler (staging has no ledger — passes nothing).
    public static CvReviewDto ToDto(
        this CvReviewResult result,
        IReadOnlyDictionary<string, string> nameByCriterionId,
        IReadOnlyDictionary<string, (string Status, DateTimeOffset? StaleAt)>? statusOverlay = null) =>
        new(
            RubricVersion: result.RubricVersion.ToString(),
            Profile: result.Profile.ToString(),
            Categories: result.Categories.Select(ToDto).ToList(),
            Verdicts: result.Verdicts.Select(v => ToDto(v, nameByCriterionId, statusOverlay)).ToList(),
            CriticalFails: result.CriticalFails.Select(v => ToDto(v, nameByCriterionId, statusOverlay)).ToList(),
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

    private static CvCriterionVerdictDto ToDto(
        CvCriterionVerdict verdict,
        IReadOnlyDictionary<string, string> nameByCriterionId,
        IReadOnlyDictionary<string, (string Status, DateTimeOffset? StaleAt)>? statusOverlay)
    {
        var status = statusOverlay is not null
            && statusOverlay.TryGetValue(verdict.CriterionId, out var s)
            ? s
            : default((string Status, DateTimeOffset? StaleAt)?);

        return new(
            CriterionId: verdict.CriterionId,
            // Fall back to the id if the rubric somehow lacks the criterion (defensive — an
            // N-1/synthetic asset); never throw on a missing heading.
            Name: nameByCriterionId.GetValueOrDefault(verdict.CriterionId, verdict.CriterionId),
            Category: verdict.Category.ToString(),
            Verdict: verdict.Verdict.ToString(),
            Evidence: verdict.Evidence.Select(ToDto).ToList(),
            NotAssessedReason: verdict.NotAssessedReason,
            UserStatus: status?.Status,
            UserStatusStaleAt: status?.StaleAt);
    }

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
