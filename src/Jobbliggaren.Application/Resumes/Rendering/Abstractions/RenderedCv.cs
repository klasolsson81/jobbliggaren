using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Rendering.Abstractions;

/// <summary>
/// A rendered CV PDF (Fas 4 STEG 10, F4-10). <paramref name="PdfBytes"/> lives in memory only —
/// never written to disk and never persisted (CTO Q6 / Invariant 3). <paramref name="Profile"/>
/// records which rendering (ATS-plain vs visual) produced it; <paramref name="Language"/> the
/// output language. The bytes never cross into a log or a plain query.
/// </summary>
public sealed record RenderedCv(
    byte[] PdfBytes,
    string ContentType,
    RenderProfile Profile,
    ResumeLanguage Language);
