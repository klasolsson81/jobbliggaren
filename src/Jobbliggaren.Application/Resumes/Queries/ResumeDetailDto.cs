namespace Jobbliggaren.Application.Resumes.Queries;

public sealed record ResumeDetailDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ResumeVersionDto> Versions,
    // The persisted template options (Fas 4b PR-8b 8b.2, ADR 0096) — the substrate that
    // drives GET /{id}/render plus the persisted-state ATS label. The mallbyggare consumer
    // was retired (CV-pivot 2026-07-16, ADR 0112); the options stay load-bearing for render.
    CvTemplateOptionsDto TemplateOptions);
