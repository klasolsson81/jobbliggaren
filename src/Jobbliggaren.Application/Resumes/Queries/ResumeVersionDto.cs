namespace Jobbliggaren.Application.Resumes.Queries;

public sealed record ResumeVersionDto(
    Guid Id,
    string Kind,
    ResumeContentDto Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
