using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

/// <summary>
/// Explicit Application-boundary mapping <see cref="ParsedResume"/> → <see cref="ParsedResumeDetailDto"/>
/// (no AutoMapper across the boundary — CLAUDE.md §5). Projects enums/value objects to their
/// transport shape; copies the parsed content verbatim (no synthesis).
/// </summary>
internal static class GetParsedResumeMapper
{
    public static ParsedResumeDetailDto ToDetailDto(this ParsedResume resume) =>
        new(
            Id: resume.Id.Value,
            Status: resume.Status.ToString(),
            DetectedLanguage: resume.DetectedLanguage.Name,
            SourceFileName: resume.SourceFileName,
            Confidence: ToConfidenceDto(resume.Confidence),
            Personnummer: new PersonnummerScanDto(
                resume.Personnummer.Found,
                resume.Personnummer.Count,
                resume.Personnummer.Kinds.Select(k => k.ToString()).ToList()),
            Content: ToContentDto(resume.Content),
            OccupationProposals: resume.OccupationProposals
                .Select(p => new OccupationProposalDto(p.ConceptId, p.Label, p.MatchedOn, p.ApproximateYears))
                .ToList(),
            CreatedAt: resume.CreatedAt,
            UpdatedAt: resume.UpdatedAt);

    private static ParseConfidenceDto ToConfidenceDto(ParseConfidence confidence) =>
        new(
            confidence.Overall.ToString(),
            confidence.RequiresManualReview,
            confidence.Fallback.ToString(),
            confidence.Sections
                .Select(s => new SectionConfidenceDto(s.Kind.ToString(), s.Level.ToString(), s.Evidence))
                .ToList());

    private static ParsedContentDto ToContentDto(ParsedResumeContent content) =>
        new(
            new ParsedContactDto(
                content.Contact.FullName, content.Contact.Email,
                content.Contact.Phone, content.Contact.Location),
            content.Profile,
            content.Experience
                .Select(e => new ParsedExperienceDto(e.Title, e.Organization, e.Period, e.RawText))
                .ToList(),
            content.Education
                .Select(e => new ParsedEducationDto(e.Institution, e.Degree, e.Period, e.RawText))
                .ToList(),
            content.Skills,
            content.Languages);
}
