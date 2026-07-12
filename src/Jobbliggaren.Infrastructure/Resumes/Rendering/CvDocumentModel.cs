using System.Globalization;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// The single source-of-truth projection both CV renderings (ATS-plain + visual) consume
/// (Fas 4 STEG 10, F4-10) — built once from the CV source so the content is identical and only the
/// rendering differs (BUILD §8.3 "samma JSON-källdata"). Two sources converge here: the parsed
/// staging <see cref="ParsedResumeContent"/> (freeform period strings) and the promoted, canonical
/// <see cref="ResumeContent"/> (structured <c>DateOnly</c> periods — TD-112 / #202). All fields are
/// nullable/empty-tolerant: a degraded parse renders an honest partial CV, never a synthesised
/// placeholder (CLAUDE.md §5). The QuestPDF <c>IDocument</c> implementations that consume this
/// model are F4-10 Phase B; this BCL-only projection ships in Phase A.
/// </summary>
/// <remarks>
/// PR-8b (8b.0) completed the promoted-content projection so NO AppCopy field is silently dropped
/// before the template work builds on it (the pre-existing gap ADR 0095 D-E deferred): the
/// Fas 4b superset — grouped skills (<see cref="SkillGroups"/>), spoken-language proficiency
/// (<see cref="LanguageLine.Proficiency"/>) and dynamic profession-driven sections
/// (<see cref="Sections"/>) — now all reach the model. The flat <see cref="Skills"/> list stays the
/// authoritative skill set (ADR 0095 D-A, DRY); groups are a presentation overlay over it, so the
/// composer renders groups plus any UNGROUPED remainder — every skill appears at least once
/// (no content loss, P2/P5). The parsed path carries no groups/proficiency/sections, so those
/// project empty/null there — an honest partial, never a fabricated grouping.
/// </remarks>
internal sealed record CvDocumentModel(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location,
    string? Profile,
    IReadOnlyList<CvDocumentModel.ExperienceLine> Experiences,
    IReadOnlyList<CvDocumentModel.EducationLine> Educations,
    IReadOnlyList<string> Skills,
    IReadOnlyList<CvDocumentModel.SkillGroupLine> SkillGroups,
    IReadOnlyList<CvDocumentModel.LanguageLine> Languages,
    IReadOnlyList<CvDocumentModel.SectionLine> Sections)
{
    internal sealed record ExperienceLine(string? Title, string? Organization, string? Period, string Text);

    internal sealed record EducationLine(string? Institution, string? Degree, string? Period, string Text);

    /// <summary>A grouped-skills overlay row (ADR 0095 D-A) — a group name and the skill names in it.</summary>
    internal sealed record SkillGroupLine(string Name, IReadOnlyList<string> Members);

    /// <summary>
    /// A spoken language with its optional localised proficiency label. <see cref="Proficiency"/> is
    /// <c>null</c> when the level is unknown (<see cref="LanguageProficiency.NotStated"/>) or the
    /// source is a parse (name-only) — an unknown level is rendered as bare name, never fabricated
    /// (ADR 0074 OQ3 / §5).
    /// </summary>
    internal sealed record LanguageLine(string Name, string? Proficiency);

    /// <summary>A dynamic profession-driven section (verbatim user heading + entries).</summary>
    internal sealed record SectionLine(string Heading, IReadOnlyList<SectionEntryLine> Entries);

    /// <summary>One entry inside a dynamic section — a title and its body lines (all verbatim).</summary>
    internal sealed record SectionEntryLine(string Title, IReadOnlyList<string> Lines);

    /// <summary>
    /// Projects the parsed content verbatim — no field is synthesised or translated. The parsed
    /// staging shape carries no grouped skills, no proficiency and no dynamic sections, so those
    /// project empty/null (an honest partial — the composer omits an empty surface, §5).
    /// </summary>
    public static CvDocumentModel From(ParsedResumeContent content) =>
        new(
            content.Contact.FullName,
            content.Contact.Email,
            content.Contact.Phone,
            content.Contact.Location,
            content.Profile,
            content.Experience
                .Select(e => new ExperienceLine(e.Title, e.Organization, e.Period, e.RawText))
                .ToList(),
            content.Education
                .Select(e => new EducationLine(e.Institution, e.Degree, e.Period, e.RawText))
                .ToList(),
            content.Skills,
            [],
            content.Languages.Select(name => new LanguageLine(name, null)).ToList(),
            []);

    /// <summary>
    /// Projects the promoted, canonical <see cref="ResumeContent"/> (TD-112 / #202) — the FULL
    /// AppCopy, no field dropped (PR-8b 8b.0, closing the ADR 0095 D-E deferral). The structured
    /// <c>DateOnly</c> periods are formatted to the same year-span display form the parsed path uses
    /// (CTO D1 / Variant A) via <see cref="FormatPeriod"/>; the experience body is the user's own
    /// <c>Description</c> (verbatim, never synthesised — §5) and education has no body. The Fas 4b
    /// superset now all projects: <see cref="ResumeContent.SkillGroups"/> → <see cref="SkillGroups"/>
    /// (the flat <see cref="ResumeContent.Skills"/> stays authoritative — DRY, ADR 0095 D-A),
    /// <see cref="SpokenLanguage.Proficiency"/> → the localised proficiency label via
    /// <paramref name="proficiencyLabel"/> (<see cref="LanguageProficiency.NotStated"/> → null, an
    /// honest bare name), and <see cref="ResumeContent.Sections"/> → <see cref="Sections"/> (verbatim
    /// user headings/entries — always shown, P4). <paramref name="ongoingLabel"/> is the localised
    /// word that closes an open-ended period; <paramref name="proficiencyLabel"/> resolves a
    /// proficiency level to its localised label (or null) — both supplied by the renderer from
    /// <see cref="CvRenderStrings"/> so this projection stays free of localisation policy.
    /// </summary>
    public static CvDocumentModel From(
        ResumeContent content,
        string ongoingLabel,
        Func<LanguageProficiency, string?> proficiencyLabel) =>
        new(
            content.PersonalInfo.FullName,
            content.PersonalInfo.Email,
            content.PersonalInfo.Phone,
            content.PersonalInfo.Location,
            content.Summary,
            content.Experiences
                .Select(e => new ExperienceLine(
                    e.Role, e.Company, FormatPeriod(e.StartDate, e.EndDate, ongoingLabel), e.Description ?? string.Empty))
                .ToList(),
            content.Educations
                .Select(e => new EducationLine(
                    e.Institution, e.Degree, FormatPeriod(e.StartDate, e.EndDate, ongoingLabel), string.Empty))
                .ToList(),
            content.Skills.Select(s => s.Name).ToList(),
            content.SkillGroups
                .Select(g => new SkillGroupLine(g.Name, g.Members))
                .ToList(),
            content.Languages
                .Select(l => new LanguageLine(l.Name, proficiencyLabel(l.Proficiency)))
                .ToList(),
            content.Sections
                .Select(s => new SectionLine(
                    s.Heading,
                    s.Entries.Select(e => new SectionEntryLine(e.Title, e.Lines)).ToList()))
                .ToList());

    // The en-dash (U+2013) range separator — the canonical year-span form the parsed periods
    // already use (e.g. "2021–2024"); NOT the §5-forbidden em-dash (U+2014).
    private const char EnDash = '–';

    /// <summary>
    /// Formats a structured period to the year-span display string (CTO D1 / Variant A) — pure,
    /// deterministic, culture-free: only the year digits and the localised
    /// <paramref name="ongoingLabel"/> appear. An open-ended period (no <paramref name="end"/>)
    /// renders "{startYear}–{ongoingLabel}"; a single-year period collapses to just that year;
    /// otherwise "{startYear}–{endYear}". Display only — the period is never scored (Goodhart/TD-B).
    /// </summary>
    internal static string FormatPeriod(DateOnly start, DateOnly? end, string ongoingLabel)
    {
        var startYear = start.Year.ToString(CultureInfo.InvariantCulture);
        if (end is null)
            return $"{startYear}{EnDash}{ongoingLabel}";

        return end.Value.Year == start.Year
            ? startYear
            : $"{startYear}{EnDash}{end.Value.Year.ToString(CultureInfo.InvariantCulture)}";
    }
}
