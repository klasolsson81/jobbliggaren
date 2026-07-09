namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// One hub-list card (Fas 4b PR-8 additions, CTO-bind Q1/Q7):
/// <see cref="OpenFindingCount"/> is the badge count read from the DEK-free
/// finding-status ledger — non-null ONLY when the resume's
/// <c>ReviewedRubricVersion</c> equals the current rubric version (null = "not
/// reviewed at the current rubric", which the UI must render as "Granska", never as
/// zero — §5 never mis-report). <see cref="Origin"/>/<see cref="Template"/> are the
/// non-PII root metadata names (ADR 0096) for the Importerad/Skapad card badges.
/// </summary>
public sealed record ResumeListItemDto(
    Guid Id,
    string Name,
    int VersionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsPrimary,
    string Language,
    string? LatestRole,
    int SectionCount,
    IReadOnlyList<string> TopSkills,
    int? OpenFindingCount,
    string Origin,
    string Template);
