namespace Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;

/// <summary>
/// A non-PII summary of the owner's most-recent PendingReview <c>ParsedResume</c> staging
/// artifact (Fas 4 onboarding decouple, ADR 0079-amendment 2026-06-23). Carries ONLY the
/// fields needed to surface a "complete your CV" card on <c>/cv</c> after the welcome flow
/// auto-reads a CV without promoting it: the id (to deep-link the gap-fill/complete page),
/// the source file name, and the upload time. No CV-PII — <see cref="SourceFileName"/> is the
/// uploaded file name (already plaintext metadata), never parsed content.
/// </summary>
public sealed record PendingParsedResumeSummaryDto(
    Guid Id,
    string SourceFileName,
    DateTimeOffset UploadedAt);
