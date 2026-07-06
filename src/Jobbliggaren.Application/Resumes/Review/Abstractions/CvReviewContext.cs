using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>Which lifecycle stage supplied the review input (ADR 0093 §D8 — two adapters, one engine).</summary>
public enum CvReviewSourceKind
{
    /// <summary>Pre-promote import artifact (<c>ParsedResume</c>) — raw extraction substrate.</summary>
    Staging,

    /// <summary>Post-promote canonical content (<c>Resume</c>/<c>ResumeContent</c>) — linearized substrate.</summary>
    Canonical,
}

/// <summary>
/// The unified review-engine input (Fas 4b PR-4, ADR 0093 §D8): "reviewable content +
/// linear text + section geometry", independent of which aggregate supplied it. Built by
/// exactly two pure adapters — <see cref="FromParsed"/> (staging) and
/// <see cref="FromCanonical"/> (canonical) — so ONE rubric engine serves both lifecycle
/// stages without forking. Carries aggregate-sourced data ONLY; the knowledge-bank lists
/// and the NLP analyzer stay injected on the engine (the port surface remains NLP-free,
/// pinned by <c>CvReviewEngineLayerTests</c>).
/// </summary>
/// <remarks>
/// Honest-capability inputs (CTO-bind PR-4 Q6): <see cref="Personnummer"/> is the real
/// scan outcome for staging and the guaranteed-clean outcome for canonical (the canonical
/// write paths run <c>ResumeContentPersonnummerGuard</c> on every save, so B4's clean
/// verdict is a KNOWN result, not an assumption). <see cref="SourceFileName"/>/
/// <see cref="SourceContentType"/>/<see cref="ParseFallback"/> are null on the canonical
/// arm (no source file until PR-9's Form C) — the rules verdict honestly on absence.
/// CV-PII in transit — never persisted, never logged (ADR 0074 Invariant 3).
/// </remarks>
public sealed record CvReviewContext(
    CvReviewSourceKind Source,
    ReviewableCv Content,
    string LinearText,
    IReadOnlyList<ParsedSectionKind> DetectedSections,
    ResumeLanguage Language,
    PersonnummerScanOutcome Personnummer,
    string? SourceFileName,
    string? SourceContentType,
    ParseFallbackReason? ParseFallback,
    // Fas 4b PR-6b — non-PII PDF layout metrics for the geometry criteria (B2 page count,
    // D9 file size, E2 whitespace). Present on the staging arm from an analyzed import; null
    // on the canonical arm (no source file until PR-9's Form C) → the rules verdict
    // NotAssessed on absence (honest ceiling, never fabricate geometry).
    CvLayoutMetrics? Layout)
{
    /// <summary>
    /// The staging adapter: the parsed CV reviews against its own raw extraction —
    /// <c>RawText</c> stays the pre-promote citation substrate (ADR 0093 §D8), the
    /// detected sections come from the parse confidence, and the period strings stay
    /// freeform (the engine date-parses them, exactly as before PR-4).
    /// </summary>
    public static CvReviewContext FromParsed(ParsedResume parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var content = parsed.Content;
        return new CvReviewContext(
            CvReviewSourceKind.Staging,
            new ReviewableCv(
                content.Contact is { } contact
                    ? new ReviewableContact(contact.FullName, contact.Email, contact.Phone, contact.Location)
                    : null,
                content.Profile,
                content.Experience
                    .Select(e => new ReviewableExperience(
                        e.Title, e.Organization, e.Period, null, null, e.RawText, TextIsDescriptionOnly: false))
                    .ToList(),
                content.Education
                    .Select(e => new ReviewableEducation(e.Institution, e.Degree))
                    .ToList(),
                content.Skills,
                content.Languages),
            parsed.RawText,
            parsed.Confidence.Sections
                .Where(s => s.Level != SectionConfidenceLevel.NotFound)
                .Select(s => s.Kind)
                .Distinct()
                .ToList(),
            parsed.DetectedLanguage,
            parsed.Personnummer,
            parsed.SourceFileName,
            parsed.SourceContentType,
            parsed.Confidence.Fallback,
            parsed.LayoutMetrics);
    }

    /// <summary>
    /// The canonical adapter: promoted/app-built content reviews against the shared
    /// linearizer's output (ADR 0093 §D8 SPOT — the caller runs
    /// <see cref="ResumeContentLinearizer.Linearize"/> first and passes the result, so
    /// this factory stays pure). Sections are known by construction, dates are
    /// structured (no period parsing), and the personnummer outcome is the
    /// guaranteed-clean one — see the type remarks for the honesty rationale.
    /// </summary>
    public static CvReviewContext FromCanonical(
        ResumeContent content, LinearizedResume linearized, ResumeLanguage language)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(linearized);
        ArgumentNullException.ThrowIfNull(language);

        return new CvReviewContext(
            CvReviewSourceKind.Canonical,
            new ReviewableCv(
                new ReviewableContact(
                    content.PersonalInfo.FullName,
                    content.PersonalInfo.Email,
                    content.PersonalInfo.Phone,
                    content.PersonalInfo.Location),
                content.Summary,
                content.Experiences
                    .Select(e => new ReviewableExperience(
                        e.Role, e.Company, null, e.StartDate, e.EndDate, e.Description ?? string.Empty,
                        TextIsDescriptionOnly: true))
                    .ToList(),
                content.Educations
                    .Select(e => new ReviewableEducation(e.Institution, e.Degree))
                    .ToList(),
                content.Skills.Select(s => s.Name).ToList(),
                content.Languages.Select(l => l.Name).ToList()),
            linearized.Text,
            MapSectionKinds(linearized.Sections),
            language,
            PersonnummerScanOutcome.None,
            SourceFileName: null,
            SourceContentType: null,
            ParseFallback: null,
            // No source file on the canonical arm until PR-9's Form C → layout NotAssessed (D-F).
            Layout: null);
    }

    // Standard linear sections map onto the parse-section vocabulary D6 verdicts on;
    // Custom sections have no standard-heading claim to make and are deliberately
    // excluded (their headings are the user's own).
    private static List<ParsedSectionKind> MapSectionKinds(IReadOnlyList<LinearSection> sections) =>
        sections
            .Where(s => s.Kind != LinearSectionKind.Custom)
            .Select(s => s.Kind switch
            {
                LinearSectionKind.Contact => ParsedSectionKind.Contact,
                LinearSectionKind.Summary => ParsedSectionKind.Profile,
                LinearSectionKind.Experience => ParsedSectionKind.Experience,
                LinearSectionKind.Education => ParsedSectionKind.Education,
                LinearSectionKind.Skills => ParsedSectionKind.Skills,
                LinearSectionKind.Languages => ParsedSectionKind.Languages,
                _ => throw new InvalidOperationException($"Unmapped linear section kind: {s.Kind}"),
            })
            .Distinct()
            .ToList();
}
