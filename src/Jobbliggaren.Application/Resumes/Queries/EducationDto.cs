namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for one canonical education entry — same honest-date-absence contract
/// as <see cref="ExperienceDto"/> (CV-pivot 2026-07-17, CTO-bind 5a-pre): nullable dates,
/// verbatim <c>RawPeriod</c> fallback, trailing optional for pre-5a client payloads.
/// </summary>
public sealed record EducationDto(
    string Institution,
    string Degree,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? RawPeriod = null);
