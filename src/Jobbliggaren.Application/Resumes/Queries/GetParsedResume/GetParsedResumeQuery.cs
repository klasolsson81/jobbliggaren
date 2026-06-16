using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

// Fas 4 STEG B / B1b. Loads the OWNING job seeker's PendingReview ParsedResume staging
// artifact (F4-8) — its decrypted, loosely-parsed CV content + confidence + pnr-scan +
// SSYK proposals — to drive the review + gap-fill UI. Read-only; nothing is synthesised
// (CLAUDE.md §5): Period stays the raw parsed string, the gap-fill form collects the
// structured dates (DQ3-3a).
//
// IRequiresFieldEncryptionKey: reads decrypted CV-PII (Form A raw_text + Form B
// parsed_content_enc) inside the warmed field-encryption pipeline (ADR 0074 Invariant 3).
// IAuthenticatedRequest gates it; ownership is enforced fail-closed in the handler
// (cross-user → null + audit, no enumeration oracle). A promoted/discarded artifact is
// invisible via the global DeletedAt filter (→ null = 404). The returned content is the
// owner's own CV and leaves the backend only to the owner's browser.
public sealed record GetParsedResumeQuery(Guid ParsedResumeId)
    : IQuery<ParsedResumeDetailDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
