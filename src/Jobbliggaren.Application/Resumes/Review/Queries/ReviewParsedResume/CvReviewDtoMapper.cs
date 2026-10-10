using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

/// <summary>
/// Explicit Application-boundary mapping <see cref="CvReviewResult"/> → <see cref="CvReviewDto"/>
/// (no AutoMapper across the boundary — CLAUDE.md §5). Projects enums to names and the
/// evidence hierarchy to its tagged transport shape.
/// </summary>
internal static class CvReviewDtoMapper
{
    // The static facts of a criterion live on the rubric, not on the verdict (which is the
    // assessment OUTCOME): its heading (Name), whether the user may ignore it (StyleOnly,
    // CTO-bind Q1) and its "Godkänns när" clause (PassCriterion, #2083). Both handlers pass
    // the rubric's criteria once and every projection is derived here from that single source
    // of truth — never a duplicated FE-side map (CLAUDE.md §10/§5).
    // statusOverlay (Fas 4b PR-4, D2(e)): criterionId → the surviving user decision, supplied
    // only by the canonical review handler (staging has no ledger — passes nothing).
    public static CvReviewDto ToDto(
        this CvReviewResult result,
        IReadOnlyList<RubricCriterion> criteria,
        IReadOnlyDictionary<string, (string Status, DateTimeOffset? StaleAt)>? statusOverlay = null)
    {
        var criterionById = criteria.ToDictionary(c => c.Id, StringComparer.Ordinal);
        return new(
            RubricVersion: result.RubricVersion.ToString(),
            Profile: result.Profile.ToString(),
            Categories: result.Categories.Select(ToDto).ToList(),
            Verdicts: result.Verdicts
                .Select(v => ToDto(v, criterionById, statusOverlay)).ToList(),
            CriticalFails: result.CriticalFails
                .Select(v => ToDto(v, criterionById, statusOverlay)).ToList(),
            AssessedCount: result.AssessedCount,
            TotalCount: result.TotalCount);
    }

    private static CvReviewCategoryDto ToDto(CvCategoryResult category) =>
        new(
            Category: category.Category.ToString(),
            PassCount: category.PassCount,
            WarnCount: category.WarnCount,
            FailCount: category.FailCount,
            NotAssessedCount: category.NotAssessedCount,
            Band: category.Band?.ToString());

    private static CvCriterionVerdictDto ToDto(
        CvCriterionVerdict verdict,
        IReadOnlyDictionary<string, RubricCriterion> criterionById,
        IReadOnlyDictionary<string, (string Status, DateTimeOffset? StaleAt)>? statusOverlay)
    {
        var status = statusOverlay is not null
            && statusOverlay.TryGetValue(verdict.CriterionId, out var s)
            ? s
            : default((string Status, DateTimeOffset? StaleAt)?);

        // Defensive: an N-1/synthetic asset may lack the criterion — never throw on it.
        var criterion = criterionById.GetValueOrDefault(verdict.CriterionId);

        return new(
            CriterionId: verdict.CriterionId,
            // Fall back to the id when the rubric lacks the criterion's heading.
            Name: criterion?.Name ?? verdict.CriterionId,
            Category: verdict.Category.ToString(),
            Verdict: verdict.Verdict.ToString(),
            Evidence: verdict.Evidence.Select(ToDto).ToList(),
            NotAssessedReason: verdict.NotAssessedReason,
            NotAssessedKind: verdict.NotAssessedKind?.ToString(),
            // Only where a pass condition was failed: on a Pass or NotAssessed row the clause
            // would read as a judgement the engine did not make (#2083, CTO F6).
            PassCriterion: verdict.Verdict is CriterionVerdict.Fail or CriterionVerdict.Warn
                ? criterion?.PassCriterion
                : null,
            UserStatus: status?.Status,
            UserStatusStaleAt: status?.StaleAt,
            IsIgnorable: criterion?.StyleOnly ?? false);
    }

    private static CitedEvidenceDto ToDto(CitedEvidence evidence) => evidence switch
    {
        TextSpanEvidence span => new CitedEvidenceDto(
            Kind: "TextSpan",
            Start: span.Span.Start,
            Length: span.Span.Length,
            Quote: span.Span.Quote,
            Note: span.Note,
            Observation: null,
            IsExcerpt: span.Span.IsExcerpt),
        StructuralEvidence structural => new CitedEvidenceDto(
            Kind: "Structural",
            Start: null,
            Length: null,
            Quote: null,
            Note: null,
            Observation: structural.Observation,
            IsExcerpt: false),
        _ => throw new ArgumentOutOfRangeException(
            nameof(evidence), evidence.GetType().Name, "Unknown CitedEvidence kind."),
    };
}
