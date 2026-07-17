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
            Content: ToContentDto(resume.Content, resume.Personnummer.Found),
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

    private static ParsedContentDto ToContentDto(ParsedResumeContent content, bool personnummerFound) =>
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
            // #844/ADR 0109: the unclassified preamble is the one content field rendered inline on
            // the review. Two-layer personnummer guard (CLAUDE.md §5, highest-priority):
            //  1. PRIMARY — fail-closed on Personnummer.Found. The Domain binding (PreambleResidue,
            //     #844) is categorical: NEVER surface a preamble from a flagged parse. The residue
            //     subtracts no personnummer (no recogniser knows the shape), and redaction re-scans
            //     the RECONSTRUCTED carrier, not the contiguous RawText the import scan flagged — so
            //     suppression, not egress-detection, is the load-bearing control.
            //  2. SECONDARY — PersonnummerRedactor on the unflagged path (belt-and-braces, parity
            //     GetResumeAtsText): a no-op when the import scan found nothing, a safety net if the
            //     two scans ever disagree.
            // Null-preserving throughout: null means "no preamble" and drives the affordance's
            // absence, so it must never collapse to "" via the redactor.
            Preamble: personnummerFound
                ? null
                : content.Preamble is { } preamble
                    ? PersonnummerRedactor.Redact(preamble)
                    : null);
}
