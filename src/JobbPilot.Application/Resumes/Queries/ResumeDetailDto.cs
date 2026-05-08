namespace JobbPilot.Application.Resumes.Queries;

public sealed record ResumeDetailDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ResumeVersionDto> Versions);
