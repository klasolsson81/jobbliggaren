using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;

// ADR 0079 STEG 3 (CV-seeded skill chips) — returns ONLY the non-PII JobTech skill
// proposals (taxonomy concept-id + canonical label) resolved deterministically at import
// time and stored as plain jsonb on the owner's PendingReview ParsedResume. Drives the
// match-setup skill section's CV-suggest for a freshly-uploaded CV that has NOT yet been
// promoted to a Resume (mirrors GetParsedResumeOccupations / #143 exactly).
//
// Deliberately NOT IRequiresFieldEncryptionKey: the handler PROJECTS the jsonb column and
// never materialises the ParsedResume aggregate, so the CV-PII shadows (raw_text Form A +
// parsed_content_enc Form B) are never read or decrypted (PII-minimisation, CLAUDE.md §5).
// IAuthenticatedRequest gates it; ownership is enforced fail-closed in the handler
// (cross-user → null + audit, no enumeration oracle); a promoted/discarded artifact is
// invisible via the global DeletedAt filter (→ null = 404).
public sealed record GetParsedResumeSkillsQuery(Guid ParsedResumeId)
    : IQuery<IReadOnlyList<SkillProposalDto>?>, IAuthenticatedRequest;
