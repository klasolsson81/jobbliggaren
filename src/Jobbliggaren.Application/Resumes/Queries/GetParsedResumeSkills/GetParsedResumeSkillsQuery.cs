using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;

// ADR 0079 STEG 3 (CV-seeded skill chips) — returns ONLY the non-PII JobTech skill
// proposals (taxonomy concept-id + canonical label) resolved deterministically at import
// time and stored as plain jsonb on the owner's ParsedResume. Drives the match-setup skill
// section's CV-suggest for a freshly-uploaded CV, whether the import left it PendingReview or
// auto-promoted it (mirrors GetParsedResumeOccupations / #143 exactly). This read is the ONLY
// skill-suggest source there is — no latest_role-style fallback was ever built — so restricting
// it to PendingReview left the skill step silent on every auto-promoted upload.
//
// Deliberately NOT IRequiresFieldEncryptionKey: the handler PROJECTS the jsonb column and
// never materialises the ParsedResume aggregate, so the CV-PII shadows (raw_text Form A +
// parsed_content_enc Form B) are never read or decrypted (PII-minimisation, CLAUDE.md §5).
// IAuthenticatedRequest gates it; ownership is enforced fail-closed in the handler
// (cross-user → null + audit, no enumeration oracle). The handler ignores the global DeletedAt
// filter and admits PendingReview and Promoted by an explicit allow-list; a DISCARDED artifact
// stays invisible (→ null = 404), because the user rejected that import.
public sealed record GetParsedResumeSkillsQuery(Guid ParsedResumeId)
    : IQuery<IReadOnlyList<SkillProposalDto>?>, IAuthenticatedRequest;
