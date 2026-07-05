namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for a grouped-skills overlay entry (Fas 4b AppCopy superset,
/// ADR 0095 D-A). <see cref="Members"/> are skill names that must also appear in
/// <see cref="ResumeContentDto.Skills"/> — the flat list stays the single skill store.
/// Nullable-with-default: STJ passes null when the JSON member is omitted (NRT is not
/// runtime-enforced), so the annotation is honest and consumers coalesce to empty.
/// </summary>
public sealed record SkillGroupDto(string Name, IReadOnlyList<string>? Members = null);
