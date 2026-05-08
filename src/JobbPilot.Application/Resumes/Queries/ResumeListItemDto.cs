namespace JobbPilot.Application.Resumes.Queries;

public sealed record ResumeListItemDto(
    Guid Id,
    string Name,
    int VersionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
