using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Domain.Privacy;
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
            content.Languages,
            // #815: free sections travel verbatim — heading and lines exactly as the user wrote
            // them. Nothing is normalised on the way out; the review shows them back as-is.
            content.Sections
                .Select(s => new ParsedSectionDto(
                    s.Heading,
                    s.Entries
                        .Select(e => new ParsedSectionEntryDto(e.Title, e.Lines))
                        .ToList()))
                .ToList(),
            // #844/ADR 0109: the unclassified preamble is the one content field rendered inline
            // on the review view, so it egresses pnr-redacted — parity with GetResumeAtsText's
            // belt-and-braces redaction (CLAUDE.md §5). Null-preserving: null means "no preamble"
            // and drives the affordance's absence, so it must not collapse to "" via the redactor.
            Preamble: content.Preamble is { } preamble
                ? PersonnummerRedactor.Redact(preamble)
                : null);
}
