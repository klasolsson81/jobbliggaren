using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;

// Fas 4 STEG 10 (F4-10, ADR 0071/0074). Renders the OWNING job seeker's parsed CV to a PDF
// (ATS-plain | visual). Port-only this STEG (no HTTP endpoint/UI yet — parity F4-8/F4-9); this
// Mediator query is the DEK-owning seam.
//
// IRequiresFieldEncryptionKey: the renderer reads the ParsedResume's decrypted CV-PII (Form A
// raw_text + Form B parsed_content_enc) — the prefetch behavior warms the owner DEK before
// materialisation, and the field-decryption interceptor decrypts on read (ADR 0049/0066; ADR
// 0074 Invariant 3). IAuthenticatedRequest gates it to the authenticated user; ownership is
// enforced in the handler (cross-user → null + audit). The rendered bytes are streamed
// compute-on-demand — never persisted (CTO Q6).
//
// Profile is the rendering profile name ("Ats" | "Visual"); the validator parses it fail-loud
// to a RenderProfile (no silent default — parity ReviewParsedResumeQuery).
public sealed record RenderCvQuery(Guid ParsedResumeId, string Profile)
    : IQuery<RenderedCvDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
