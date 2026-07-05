namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The unified, source-agnostic content view the review rules read (Fas 4b PR-4,
/// ADR 0093 §D8 — "reviewable content", the first leg of the D8 triple). A superset
/// projection over BOTH source shapes: the tolerant staging <c>ParsedResumeContent</c>
/// and the strict canonical <c>ResumeContent</c>. Member names follow the staging
/// content's established rule-surface vocabulary (Contact/Profile/Experience/Education).
/// CV-PII in transit — never persisted, never logged (ADR 0074 Invariant 3).
/// </summary>
public sealed record ReviewableCv(
    ReviewableContact? Contact,
    string? Profile,
    IReadOnlyList<ReviewableExperience> Experience,
    IReadOnlyList<ReviewableEducation> Education,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages);

/// <summary>Contact fields, all optional — staging tolerates gaps; B3 verdicts on them.</summary>
public sealed record ReviewableContact(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location);

/// <summary>
/// One work-experience entry in the unified view. The two arms fill it differently and
/// <see cref="TextIsDescriptionOnly"/> records which contract <see cref="Text"/> honors:
/// staging supplies the segmenter's verbatim block (header line + period line +
/// description; <c>false</c>) with the freeform <see cref="PeriodText"/>; canonical
/// supplies the pure description (<c>true</c>) with structured
/// <see cref="StartDate"/>/<see cref="EndDate"/> (open end = ongoing).
/// </summary>
public sealed record ReviewableExperience(
    string? Title,
    string? Organization,
    string? PeriodText,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Text,
    bool TextIsDescriptionOnly);

/// <summary>One education entry — A10 verdicts on institution + degree presence.</summary>
public sealed record ReviewableEducation(
    string? Institution,
    string? Degree);
