using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;

// Fas 4b PR-4 (#653, ADR 0093 §D8): the deterministic CV review for a PROMOTED/app-built
// canonical Resume — the same rubric engine as the staging review, reached through the
// canonical adapter (CvReviewContext.FromCanonical over the shared linearizer). Review
// RESULTS stay compute-on-demand (ADR 0074); the response merges the persisted
// finding-status overlay (D2(e)) onto the freshly computed verdicts.
//
// IRequiresFieldEncryptionKey: reads the Master version's decrypted content
// (Form B content_enc) — FieldEncryptionKeyPrefetchBehavior warms the owner DEK before
// materialisation (ADR 0049/0066; ADR 0074 Invariant 3). IAuthenticatedRequest gates it
// to the authenticated user; ownership is enforced in the handler (cross-user → null + audit).
public sealed record ReviewResumeQuery(Guid ResumeId, string Profile)
    : IQuery<CvReviewDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
