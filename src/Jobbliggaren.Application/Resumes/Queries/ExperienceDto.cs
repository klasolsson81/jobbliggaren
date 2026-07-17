namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for one canonical experience entry. <c>StartDate</c> is nullable and
/// <c>RawPeriod</c> carries the verbatim period string from the user's own file — the
/// honest-date-absence contract (CV-pivot 2026-07-17, CTO-bind 5a-pre): structured dates
/// are authoritative when present; RawPeriod is a display/citation fallback only.
/// <c>RawPeriod</c> is a trailing optional so a pre-5a client payload (which omits it)
/// deserialises cleanly.
/// </summary>
public sealed record ExperienceDto(
    string Company,
    string Role,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Description,
    string? RawPeriod = null);
