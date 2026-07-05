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
internal sealed record CvDocumentModel(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location,
    string? Profile,
    IReadOnlyList<CvDocumentModel.ExperienceLine> Experiences,
    IReadOnlyList<CvDocumentModel.EducationLine> Educations,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages)
{
    internal sealed record ExperienceLine(string? Title, string? Organization, string? Period, string Text);

    internal sealed record EducationLine(string? Institution, string? Degree, string? Period, string Text);

    /// <summary>Projects the parsed content verbatim — no field is synthesised or translated.</summary>
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
            content.Languages);

    /// <summary>
    /// Projects the promoted, canonical <see cref="ResumeContent"/> (TD-112 / #202). The structured
    /// <c>DateOnly</c> periods are formatted to the same year-span display form the parsed path
    /// already uses (CTO D1 / Variant A) via <see cref="FormatPeriod"/>; the experience body is the
    /// user's own <c>Description</c> (verbatim, never synthesised — §5) and education has no body.
    /// Since the Fas 4b superset (ADR 0095 D-C), the promoted content carries spoken languages, so
    /// their names feed the existing languages slot (an empty list still renders as an honest
    /// partial — the composer omits the empty section). The proficiency level and the other superset
    /// fields (skill groups, dynamic sections) are not rendered yet — their render surfaces are a
    /// later PR (ADR 0095 D-E). <paramref name="ongoingLabel"/> is the localised word that closes an
    /// open-ended period (resolved by the renderer from <see cref="CvRenderStrings.Labels.Ongoing"/>).
    /// </summary>
    public static CvDocumentModel From(ResumeContent content, string ongoingLabel) =>
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
            content.Languages.Select(l => l.Name).ToList());

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
