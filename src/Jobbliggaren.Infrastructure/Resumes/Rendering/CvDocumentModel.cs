using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// The single source-of-truth projection both CV renderings (ATS-plain + visual) consume
/// (Fas 4 STEG 10, F4-10) — built once from <see cref="ParsedResumeContent"/> so the content is
/// identical and only the rendering differs (BUILD §8.3 "samma JSON-källdata"). All fields are
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
}
