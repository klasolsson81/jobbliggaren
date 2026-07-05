namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for a dynamic profession-driven CV section (Fas 4b AppCopy superset,
/// ADR 0095 D-B). <see cref="Heading"/> is free user text. Nullable-with-default: STJ
/// passes null when the JSON member is omitted (NRT is not runtime-enforced), so the
/// annotation is honest and consumers coalesce to empty.
/// </summary>
public sealed record ResumeSectionDto(string Heading, IReadOnlyList<SectionEntryDto>? Entries = null);
