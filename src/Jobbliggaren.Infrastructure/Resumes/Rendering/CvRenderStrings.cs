using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// The CV renderer's localised STRUCTURAL labels (Fas 4 STEG 10, F4-10) — Swedish + English
/// section headings (BUILD §8.3 "svensk och engelsk output"). Only the labels are localised;
/// the user's CV content is rendered verbatim (translating it would be synthesis, §5). The same
/// canonical-heading set is the single source for the F4-10 heading-normalisation transform
/// (so "standard heading" is defined once, not hardcoded per consumer).
/// </summary>
internal static class CvRenderStrings
{
    /// <summary>
    /// The localised section heading labels for one output language, plus the non-heading
    /// <c>Ongoing</c> token (TD-112 / #202) — the word that closes an open-ended
    /// period (EndDate == null) when rendering a promoted Resume's structured dates, e.g.
    /// "2021–pågående". It is deliberately NOT a section heading, so it is excluded from
    /// <see cref="SectionHeadings"/> (the membership set for heading normalisation).
    /// </summary>
    internal sealed record Labels(
        string Contact,
        string Profile,
        string Experience,
        string Education,
        string Skills,
        string Languages,
        string Ongoing);

    private static readonly Labels Swedish =
        new("Kontakt", "Profil", "Arbetslivserfarenhet", "Utbildning", "Kompetenser", "Språk", "pågående");

    private static readonly Labels English =
        new("Contact", "Profile", "Experience", "Education", "Skills", "Languages", "present");

    /// <summary>The label set for the CV's detected language (English → English, else Swedish).</summary>
    public static Labels For(ResumeLanguage language) =>
        language == ResumeLanguage.En ? English : Swedish;

    /// <summary>
    /// The localised display label for a self-declared language proficiency, or <c>null</c> when the
    /// level is <see cref="LanguageProficiency.NotStated"/> — an unknown level renders as a bare
    /// language name, never a fabricated "basic" (ADR 0074 OQ3 / §5). The SmartEnum <c>Name</c> tokens
    /// are English code identifiers; the user-facing words resolve here (Swedish per the enum's
    /// documented UI vocabulary: Grundläggande/God/Flytande/Modersmål). English keeps the plain terms.
    /// </summary>
    public static string? ProficiencyLabel(LanguageProficiency proficiency, ResumeLanguage language)
    {
        if (proficiency == LanguageProficiency.NotStated)
            return null;

        var swedish = language != ResumeLanguage.En;
        return proficiency.Name switch
        {
            nameof(LanguageProficiency.Basic) => swedish ? "Grundläggande" : "Basic",
            nameof(LanguageProficiency.Good) => swedish ? "God" : "Good",
            nameof(LanguageProficiency.Fluent) => swedish ? "Flytande" : "Fluent",
            nameof(LanguageProficiency.Native) => swedish ? "Modersmål" : "Native",
            _ => null,
        };
    }

    /// <summary>The canonical section headings for a language — the membership set the
    /// heading-normalisation transform recognises as "a standard heading".</summary>
    public static IReadOnlyList<string> SectionHeadings(ResumeLanguage language)
    {
        var labels = For(language);
        return
        [
            labels.Contact,
            labels.Profile,
            labels.Experience,
            labels.Education,
            labels.Skills,
            labels.Languages,
        ];
    }
}
