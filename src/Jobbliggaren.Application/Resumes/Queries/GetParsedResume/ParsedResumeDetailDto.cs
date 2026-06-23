using Jobbliggaren.Application.Resumes.Commands.ImportResume;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

/// <summary>
/// Full detail view of a PendingReview <c>ParsedResume</c> staging artifact (F4-8), used to
/// drive the review + gap-fill UI (Fas 4 STEG B). Carries the owner's decrypted, loosely
/// parsed CV content faithfully — every field is honest about what the deterministic parser
/// found and nothing is synthesised (CLAUDE.md §5): each experience/education keeps its raw
/// <c>Period</c> string (not a guessed date) so the gap-fill form collects structured dates
/// downstream (DQ3-3a). This is CV-PII; the handler enforces owner-only access fail-closed
/// (IDOR → 404 + audit) and reads it inside the field-encryption pipeline (Invariant 3).
/// Reuses the parse-summary read-models (<see cref="ParseConfidenceDto"/>,
/// <see cref="PersonnummerScanDto"/>) already defined for the import response.
/// </summary>
public sealed record ParsedResumeDetailDto(
    Guid Id,
    string Status,
    string DetectedLanguage,
    string SourceFileName,
    ParseConfidenceDto Confidence,
    PersonnummerScanDto Personnummer,
    ParsedContentDto Content,
    IReadOnlyList<OccupationProposalDto> OccupationProposals,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The loosely parsed CV content — best-effort, often partial; never synthesised.</summary>
public sealed record ParsedContentDto(
    ParsedContactDto Contact,
    string? Profile,
    IReadOnlyList<ParsedExperienceDto> Experiences,
    IReadOnlyList<ParsedEducationDto> Educations,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Languages);

public sealed record ParsedContactDto(string? FullName, string? Email, string? Phone, string? Location);

/// <summary>One experience entry — best-effort structured fields plus the verbatim entry
/// text. <c>Period</c> is the raw parsed string (e.g. "2021–2024"); the gap-fill form turns
/// it into structured dates (the backend never guesses dates on a PII field — DQ3-3a).</summary>
public sealed record ParsedExperienceDto(string? Title, string? Organization, string? Period, string RawText);

public sealed record ParsedEducationDto(string? Institution, string? Degree, string? Period, string RawText);

/// <summary>An unconfirmed SSYK occupation-group proposal (ADR 0040 Beslut 4 — the user
/// confirms downstream; never auto-selected). Non-PII (taxonomy id + labels).
/// <see cref="ApproximateYears"/> (ADR 0079-amendment) is the CV-derived ~years of experience
/// attributed to this group at import (null = "not stated"); it seeds the wizard's per-occupation
/// year input (PR-4). A non-PII integer projection — the raw periods stay DEK-encrypted.</summary>
public sealed record OccupationProposalDto(string ConceptId, string Label, string MatchedOn, int? ApproximateYears);
