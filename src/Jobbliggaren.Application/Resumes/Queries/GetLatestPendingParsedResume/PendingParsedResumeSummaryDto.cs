namespace Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;

/// <summary>
/// A non-PII summary of the owner's most-recent PendingReview <c>ParsedResume</c> staging
/// artifact (Fas 4 onboarding decouple, ADR 0079-amendment 2026-06-23). Carries ONLY the
/// fields needed to surface a "complete your CV" card on <c>/cv</c> after the welcome flow
/// auto-reads a CV without promoting it: the id (to deep-link the gap-fill/complete page),
/// the source file name, and the upload time. No CV-PII — <see cref="SourceFileName"/> is the
/// uploaded file name (plaintext metadata, never parsed content), and any personnummer-shaped
/// span in it is masked at <c>ParsedResume.Create</c> (#465), so this projection cannot surface
/// a plaintext personnummer.
/// </summary>
public sealed record PendingParsedResumeSummaryDto(
    Guid Id,
    string SourceFileName,
    DateTimeOffset UploadedAt,
    // Fas 4b PR-8 (CTO-bind Q5): the confirm-task presence flags behind the action
    // card's "X av Y uppgifter klara" meter — denormalized non-PII booleans computed at
    // import (ADR 0059), projected plainly like the fields above (this query still
    // never decrypts CV-PII). Null for pre-PR-8 imports: "not computed", the card
    // renders without a meter rather than guessing.
    ParsedGapSummaryDto? Gaps);

/// <summary>
/// Mirror of the Domain <c>ParsedGapSummary</c> presence flags (Fas 4b PR-8, CTO-bind
/// Q5). The task definition is shared with the Slutför-guide's step gate — the meter
/// and the guide must never disagree about what counts as a task.
/// </summary>
public sealed record ParsedGapSummaryDto(
    bool HasFullName,
    bool HasEmail,
    bool HasPhone,
    bool HasLocation,
    bool HasProfile,
    bool HasExperience,
    bool HasEducation,
    bool HasSkills,
    bool HasLanguages);
