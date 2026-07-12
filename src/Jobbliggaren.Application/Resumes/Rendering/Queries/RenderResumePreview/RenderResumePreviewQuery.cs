using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResumePreview;

// Fas 4b PR-8b 8b.3 (CTO-bind Q1, Variant B — docs/reviews/2026-07-12-fas4b-pr8b-8b3-cto.md).
// Ephemeral live-preview of a promoted, canonical Resume rendered with UNSAVED template options —
// the mallbyggare's "Uppdatera förhandsvisning". Distinct from RenderResumeQuery (which renders the
// PERSISTED resume.TemplateOptions): here the four visual options travel WITH the request and are
// composed over the persisted photo config; NOTHING is persisted (preview == export via the shared
// ICvRenderer, ADR 0093 doctrine). A dedicated query, NOT a mode flag on the canonical render
// (CCP/ISP — the canonical /{id}/render is a stable, shared surface [ResumeCard, export-adjacent];
// the builder preview is a volatile surface whose option set will grow). Parity
// PreviewCvImprovementQuery (a dedicated never-persist preview).
//
// Visual-ONLY by construction: RenderProfile.Ats always ignores the template (plain single-column
// parallel), so the four options move nothing on an Ats render — a knob that changes nothing is a
// dishonest surface (§5). The builder's ATS view reuses the invariant /{id}/ats-text.
//
// IRequiresFieldEncryptionKey / IAuthenticatedRequest / owner-scope in the handler: the SAME
// warmed-DEK owner-scoped read as RenderResumeQuery (the Master ResumeVersion.Content is decrypted
// on read); cross-user → null + audit. The rendered bytes are streamed compute-on-demand — never
// persisted (Invariant 3, CTO Q6).
//
// The four option names are validated fail-loud to their closed SmartEnum sets by the validator
// (parity ChangeTemplateOptionsCommandValidator); the handler degrades a bad name to null
// (defense-in-depth), never an unmapped throw.
public sealed record RenderResumePreviewQuery(
    Guid ResumeId, string Template, string AccentColor, string FontPair, string Density)
    : IQuery<RenderedCvDto?>, IAuthenticatedRequest, IRequiresFieldEncryptionKey;
