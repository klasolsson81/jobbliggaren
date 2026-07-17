namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// One canonical work-experience entry. <see cref="StartDate"/> is nullable because the
/// canonical model admits HONEST date absence (CV-pivot 2026-07-17, CTO-bind 5a-pre): the
/// parser never guesses dates (house promise), so a verbatim auto-promoted entry carries
/// none until the user supplies them. <see cref="RawPeriod"/> preserves the period string
/// the user's own file carried ("2019–2022") — a display/citation fallback used only when
/// structured dates are absent, never written by the structured editor and never itself
/// scored. Structured dates are authoritative when present.
/// </summary>
public sealed record Experience(
    string Company,
    string Role,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Description,
    string? RawPeriod = null);
