using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;

// Fas 4 onboarding decouple (ADR 0079-amendment 2026-06-23, senior-cto-advisor pending-card bind).
// Returns a non-PII SUMMARY of the CURRENT user's most-recent PendingReview parsed CV — the staging
// artifact produced by the welcome upload that has NOT yet been promoted to a canonical Resume. The
// welcome flow no longer forces a gap-fill step; instead the CV is read-and-kept as a PendingReview
// artifact and surfaced on /cv as a "complete your CV" card (PR-3) that deep-links the existing
// gap-fill/complete page. Null = the user has no pending CV (a normal, non-error state).
//
// Deliberately NOT IRequiresFieldEncryptionKey and not id-addressed: the handler PROJECTS the
// plaintext metadata columns (id / source_file_name / created_at) and never materialises the
// ParsedResume aggregate, so the CV-PII shadows (raw_text Form A + parsed_content_enc Form B) are
// never read or decrypted (PII-minimisation, CLAUDE.md §5 — mirrors the occupations/skills
// projections). Owner-scope is the only access rule (no client-supplied id → no IDOR surface, no
// enumeration oracle); a promoted/discarded artifact is invisible via the global DeletedAt filter.
public sealed record GetLatestPendingParsedResumeQuery
    : IQuery<PendingParsedResumeSummaryDto?>, IAuthenticatedRequest;
