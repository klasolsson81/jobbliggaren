using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

// Fas 4 STEG 9 (F4-9, ADR 0071/0074). Runs the deterministic CV review for the OWNING
// job seeker.
//
// IRequiresFieldEncryptionKey: the review reads the ParsedResume's decrypted CV-PII
// (Form A raw_text + Form B parsed_content_enc) — FieldEncryptionKeyPrefetchBehavior warms
// the owner DEK before materialisation, and the field-decryption interceptor decrypts on
// read (ADR 0049/0066; ADR 0074 Invariant 3). IAuthenticatedRequest gates it to the
// authenticated user; ownership is enforced in the handler (cross-user → null + audit).
//
// Profile is the rendering profile name ("Ats" | "Visual"); the validator parses it
// fail-loud to a RenderProfile (no silent default — parity RubricVersion.Parse).
public sealed record ReviewParsedResumeQuery(Guid ParsedResumeId, string Profile)
    : IQuery<CvReviewDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
