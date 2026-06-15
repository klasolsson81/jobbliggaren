using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Rendering.Abstractions;

/// <summary>
/// Renders a parsed CV to a PDF (Fas 4 STEG 10, F4-10) — ATS-plain and visual from the SAME
/// <c>ParsedResumeContent</c> JSON source (BUILD §8.3), so the content criteria are identical
/// and only the rendering differs. Pure: takes an already-decrypted <see cref="ParsedResume"/>
/// (Invariant 3 owned by the read-handler), produces bytes in memory, and streams them
/// compute-on-demand (CTO Q6 — no artifact persistence). The QuestPDF dependency is confined to
/// the Infrastructure implementation; this port is BCL-only. (Implementation is F4-10 Phase B.)
/// </summary>
public interface ICvRenderer
{
    ValueTask<RenderedCv> RenderAsync(
        ParsedResume parsedResume,
        RenderProfile profile,
        CancellationToken cancellationToken);
}
