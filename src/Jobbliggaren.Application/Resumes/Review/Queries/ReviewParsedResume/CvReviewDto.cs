using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

/// <summary>
/// Flat transport DTO for a CV review (Fas 4 STEG 9, F4-9) — the Application result
/// type (<see cref="CvReviewResult"/>) never crosses the Application boundary (CLAUDE.md
/// §2.3). Enums are projected to their names and the rubric version to its string; the
/// evidence hierarchy is flattened to a tagged shape so the client can render spans vs
/// structural facts without the discriminated-union type. No opaque total (Goodhart).
/// </summary>
public sealed record CvReviewDto(
    string RubricVersion,
    string Profile,
    IReadOnlyList<CvReviewCategoryDto> Categories,
    IReadOnlyList<CvCriterionVerdictDto> Verdicts,
    IReadOnlyList<CvCriterionVerdictDto> CriticalFails,
    int AssessedCount,
    int TotalCount);

/// <summary>Per-category verdict counts (primary) + the data-derived band (secondary).</summary>
public sealed record CvReviewCategoryDto(
    string Category,
    int PassCount,
    int WarnCount,
    int FailCount,
    int NotAssessedCount,
    string Band);

/// <summary>
/// One criterion's verdict with its cited evidence, projected for transport.
/// <para>
/// <see cref="Name"/> is the human-readable Swedish criterion heading (from the rubric,
/// e.g. "Mätbara resultat") — the UI leads with it instead of the cryptic id so the
/// review reads for a job-seeker, not a developer (CLAUDE.md §10). <see cref="CriterionId"/>
/// (e.g. "A1") rides along as a de-emphasised support reference, never the primary label.
/// </para>
/// <para>
/// <see cref="UserStatus"/>/<see cref="UserStatusStaleAt"/> (Fas 4b PR-4, ADR 0093
/// §D2(e)) carry the persisted finding-status overlay on the CANONICAL review — the
/// status name ("Resolved"/"Ignored"/"Open") the user recorded, and the staleness stamp
/// when the CV changed under a Resolved decision that is still present. Null on the
/// staging review and when no (surviving) decision exists.
/// </para>
/// </summary>
public sealed record CvCriterionVerdictDto(
    string CriterionId,
    string Name,
    string Category,
    string Verdict,
    IReadOnlyList<CitedEvidenceDto> Evidence,
    string? NotAssessedReason,
    string? UserStatus = null,
    DateTimeOffset? UserStatusStaleAt = null);

/// <summary>
/// Tagged transport form of <see cref="CitedEvidence"/>: <c>Kind</c> is "TextSpan" or
/// "Structural". For "TextSpan" the span fields are set; for "Structural" only
/// <c>Observation</c> is set.
/// </summary>
public sealed record CitedEvidenceDto(
    string Kind,
    int? Start,
    int? Length,
    string? Quote,
    string? Note,
    string? Observation);
