namespace Jobbliggaren.Application.Resumes.Queries;

public sealed record ResumeDetailDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ResumeVersionDto> Versions,
    // The persisted template options (Fas 4b PR-8b 8b.2, ADR 0096) — hydrates the
    // template builder's current selection + the persisted-state ATS label (8b.3).
    CvTemplateOptionsDto TemplateOptions);
