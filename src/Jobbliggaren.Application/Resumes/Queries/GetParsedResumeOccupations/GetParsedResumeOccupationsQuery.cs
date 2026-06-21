using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;

// Fas 4 onboarding (ADR 0076/0040, CTO Variant B 2026-06-21) — returns ONLY the non-PII SSYK
// occupation proposals (taxonomy id + labels) already derived deterministically at import time
// and stored as plain jsonb on the owner's PendingReview ParsedResume (F4-8). Drives the
// match-setup wizard's CV-suggest for a freshly-uploaded CV that has NOT yet been promoted to a
// Resume — the wizard's latest_role path (suggestOccupationsFromCvAction) only covers promoted
// CVs, so a brand-new user who just uploaded sees "noCv" without this read.
//
// Deliberately NOT IRequiresFieldEncryptionKey: the handler PROJECTS the jsonb column and never
// materialises the ParsedResume aggregate, so the CV-PII shadows (raw_text Form A +
// parsed_content_enc Form B) are never read or decrypted (PII-minimisation, CLAUDE.md §5 —
// decrypting PII we never use is the anti-pattern Variant A was rejected for). IAuthenticatedRequest
// gates it; ownership is enforced fail-closed in the handler (cross-user → null + audit, no
// enumeration oracle); a promoted/discarded artifact is invisible via the global DeletedAt filter
// (→ null = 404).
public sealed record GetParsedResumeOccupationsQuery(Guid ParsedResumeId)
    : IQuery<IReadOnlyList<OccupationProposalDto>?>, IAuthenticatedRequest;
