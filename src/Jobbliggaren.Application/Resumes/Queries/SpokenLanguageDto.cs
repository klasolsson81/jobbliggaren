namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for a spoken language (Fas 4b AppCopy superset, ADR 0095 D-C).
/// <see cref="Proficiency"/> carries the <c>LanguageProficiency</c> SmartEnum <b>Name</b>
/// token (English: NotStated/Basic/Good/Fluent/Native); the Swedish UI label is resolved
/// at the frontend. An unknown/absent token maps to <c>NotStated</c> (tolerant, never
/// synthesised) in <c>ResumeContentMapper</c>.
/// </summary>
public sealed record SpokenLanguageDto(string Name, string Proficiency);
