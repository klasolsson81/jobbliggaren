namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// One canonical education entry. Dates are nullable and <see cref="RawPeriod"/> carries
/// the verbatim period string as a display/citation fallback — same honest-date-absence
/// contract as <see cref="Experience"/> (CV-pivot 2026-07-17, CTO-bind 5a-pre): structured
/// dates are authoritative when present; RawPeriod is used only when they are absent and
/// is never scored.
/// </summary>
public sealed record Education(
    string Institution,
    string Degree,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? RawPeriod = null);
