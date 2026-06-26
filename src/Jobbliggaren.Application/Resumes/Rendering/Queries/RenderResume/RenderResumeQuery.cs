using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;

// TD-112 / #202. Renders the OWNING job seeker's PROMOTED, canonical Resume to a PDF (ATS-plain |
// visual) — the render-by-Resume-id path that the parsed-only render (RenderCvQuery) lacked, so
// the promoted Resume surface (ResumeCard) can preview without a parsedId. Reuses the shared
// RenderedCvDto transport (the result type RenderedCv never crosses the Application boundary,
// CLAUDE.md §2.3).
//
// IRequiresFieldEncryptionKey: the renderer reads the Resume's Master ResumeVersion.Content
// (encrypted content_enc shadow, ADR 0049 #1c) — the prefetch behavior warms the owner DEK
// before materialisation and the field-decryption interceptor decrypts on read (ADR 0049/0066;
// ADR 0074 Invariant 3). IAuthenticatedRequest gates it to the authenticated user; ownership is
// enforced in the handler (cross-user → null + audit). The rendered bytes are streamed
// compute-on-demand — never persisted (CTO Q6), parity RenderCvQuery.
//
// Profile is the rendering profile name ("Ats" | "Visual"); the validator parses it fail-loud to
// a RenderProfile (no silent default — parity RenderCvQuery).
public sealed record RenderResumeQuery(Guid ResumeId, string Profile)
    : IQuery<RenderedCvDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
