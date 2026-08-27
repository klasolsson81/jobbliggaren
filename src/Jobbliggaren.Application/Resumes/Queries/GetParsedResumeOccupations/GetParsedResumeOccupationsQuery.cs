using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;

// Fas 4 onboarding (ADR 0076/0040, CTO Variant B 2026-06-21) — returns ONLY the non-PII SSYK
// occupation proposals (taxonomy id + labels) already derived deterministically at import time
// and stored as plain jsonb on the owner's ParsedResume (F4-8). Drives the match-setup wizard's
// CV-suggest for a freshly-uploaded CV, whether the import left it PendingReview or auto-promoted
// it. BOTH arms read here: the endpoint auto-promotes every upload and Promote() soft-deletes the
// artifact, so restricting this read to PendingReview made the proposals unreachable on the
// ordinary path. The latest_role fallback cannot stand in for it — that is ONE denormalised
// string, while these proposals are the import's own union over education and experience, and a
// CV whose most recent entry names an employer derives nothing from it.
//
// Deliberately NOT IRequiresFieldEncryptionKey: the handler PROJECTS the jsonb column and never
// materialises the ParsedResume aggregate, so the CV-PII shadows (raw_text Form A +
// parsed_content_enc Form B) are never read or decrypted (PII-minimisation, CLAUDE.md §5 —
// decrypting PII we never use is the anti-pattern Variant A was rejected for). IAuthenticatedRequest
// gates it; ownership is enforced fail-closed in the handler (cross-user → null + audit, no
// enumeration oracle). The handler ignores the global DeletedAt filter and admits PendingReview
// and Promoted by an explicit allow-list; a DISCARDED artifact stays invisible (→ null = 404),
// because the user rejected that import.
public sealed record GetParsedResumeOccupationsQuery(Guid ParsedResumeId)
    : IQuery<IReadOnlyList<OccupationProposalDto>?>, IAuthenticatedRequest;
