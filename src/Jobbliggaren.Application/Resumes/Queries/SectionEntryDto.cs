namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for one entry in a dynamic CV section (Fas 4b AppCopy superset,
/// ADR 0094 D-B). Nullable-with-default: STJ passes null when the JSON member is
/// omitted (NRT is not runtime-enforced), so the annotation is honest and consumers
/// coalesce to empty.
/// </summary>
public sealed record SectionEntryDto(string Title, IReadOnlyList<string>? Lines = null);
