namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;

/// <summary>
/// Flat transport DTO for a rendered CV (Fas 4 STEG 10, F4-10) — the Application result type
/// (<c>RenderedCv</c>) never crosses the Application boundary (CLAUDE.md §2.3). The bytes are
/// streamed to the owner compute-on-demand and never persisted (CTO Q6 / Invariant 3).
/// </summary>
public sealed record RenderedCvDto(
    byte[] PdfBytes,
    string ContentType,
    string Profile,
    string Language);
