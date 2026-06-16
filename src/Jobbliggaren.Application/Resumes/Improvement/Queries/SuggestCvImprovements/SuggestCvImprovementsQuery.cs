using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;

// Fas 4 STEG 10 (F4-10, ADR 0071/0074). Produces deterministic propose-and-approve diffs for
// the OWNING job seeker's parsed CV. Exposed over HTTP since STEG B (GET .../parsed/{id}/improvements,
// PR #113); the transmitted result is personnummer-redacted at the engine choke point (STEG B-2,
// ADR 0074 Inv. 1). This Mediator query is the seam the endpoint + integration tests drive.
//
// IRequiresFieldEncryptionKey: the engine reads the ParsedResume's decrypted CV-PII (Form A
// raw_text + Form B parsed_content_enc) — FieldEncryptionKeyPrefetchBehavior warms the owner
// DEK before materialisation, and the field-decryption interceptor decrypts on read (ADR
// 0049/0066; ADR 0074 Invariant 3). IAuthenticatedRequest gates it to the authenticated user;
// ownership is enforced in the handler (cross-user → null + audit).
//
// Profile is the rendering profile name ("Ats" | "Visual"); the validator parses it fail-loud
// to a RenderProfile (no silent default — parity ReviewParsedResumeQuery).
public sealed record SuggestCvImprovementsQuery(Guid ParsedResumeId, string Profile)
    : IQuery<CvImprovementDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
