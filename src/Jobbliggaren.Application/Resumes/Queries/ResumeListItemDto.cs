namespace Jobbliggaren.Application.Resumes.Queries;

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
    IReadOnlyList<string> TopSkills);
