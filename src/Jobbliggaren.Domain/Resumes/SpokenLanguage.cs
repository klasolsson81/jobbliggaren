namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// A spoken language on a canonical CV (Fas 4b AppCopy superset <c>sprak</c>, ADR 0093 D1 /
/// LRM ADR 0094 D-C). <paramref name="Name"/> is the language name as free user text (e.g.
/// "Svenska", "Engelska") — it is CV-PII free text and is scanned by
/// <c>ResumeContentPersonnummerGuard</c> like every other free-text field. Proficiency is a
/// closed, typed vocabulary (<see cref="LanguageProficiency"/>), defaulting to
/// <see cref="LanguageProficiency.NotStated"/> for imported languages.
/// </summary>
public sealed record SpokenLanguage(string Name, LanguageProficiency Proficiency);
