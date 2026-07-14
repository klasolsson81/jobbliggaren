using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Sections.Queries.GetCvSectionSuggestions;

// Fas 4b 8b.4a (ADR 0107) — occupation-driven section suggestions for the Slutför guide's
// "Lägg till sektion" panel. The chain: the user's CONFIRMED occupation choice
// (MatchPreferences.PreferredOccupationGroups, ssyk-4) → the taxonomy's group→field edge →
// the branschgrupp asset → the section rule-table. Deterministic, no AI/LLM (ADR 0071).
//
// This is a READ SLICE, not a ProposedChange. A section suggestion has no Before, no After and
// no transform — it is not a diff, so it does not belong in the improvement pipeline (CTO bind
// Q1-B, 2026-07-13). Emitting it as a `SectionReorder` would have been a semantic mislabel: the
// FE renders that kind as "Ändra sektionsordning", which is not what this proposes.
//
// IRequiresFieldEncryptionKey: the handler reads the ParsedResume's decrypted CV content (Form B
// parsed_content_enc) to answer "does she already HAVE this section?" — the prefetch behavior
// warms the owner DEK before materialisation (ADR 0049/0066; ADR 0074 Invariant 3), parity
// SuggestCvImprovementsQuery. IAuthenticatedRequest gates it to the authenticated user;
// ownership is enforced in the handler (cross-user → null + audit).
public sealed record GetCvSectionSuggestionsQuery(Guid ParsedResumeId)
    : IQuery<CvSectionSuggestionsDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
