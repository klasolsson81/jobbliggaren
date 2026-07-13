namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for one entry in a dynamic CV section (Fas 4b AppCopy superset,
/// ADR 0095 D-B). Nullable-with-default: STJ passes null when the JSON member is
/// omitted (NRT is not runtime-enforced), so the annotation is honest and consumers
/// coalesce to empty.
/// </summary>
/// <remarks>
/// #815: <c>Title</c> is nullable, mirroring <c>SectionEntry</c>. An entry with lines and no
/// heading ("Referenser / Lämnas på begäran.") is an ordinary CV shape, and the parser will not
/// invent a title for it (ADR 0071). The mapper passes the absence through verbatim — it does not
/// normalise <c>null</c> to <c>""</c> or back; consumers test with <c>IsNullOrWhiteSpace</c>.
/// </remarks>
public sealed record SectionEntryDto(string? Title, IReadOnlyList<string>? Lines = null);
