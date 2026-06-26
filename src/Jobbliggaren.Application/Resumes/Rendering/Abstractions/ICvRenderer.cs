using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Rendering.Abstractions;

/// <summary>
/// Renders a CV to a PDF (Fas 4 STEG 10, F4-10) — ATS-plain and visual from the SAME projection
/// (BUILD §8.3), so the content criteria are identical and only the rendering differs. Pure:
/// takes already-decrypted content (Invariant 3 owned by the read-handler), produces bytes in
/// memory, and streams them compute-on-demand (CTO Q6 — no artifact persistence). The QuestPDF
/// dependency is confined to the Infrastructure implementation; this port is BCL-only.
/// Two source shapes converge on the same renderer:
/// <list type="bullet">
/// <item>the <see cref="ParsedResume"/> staging artifact (freeform period strings, detected
/// language) — the parsed-CV preview surface;</item>
/// <item>the promoted, canonical <see cref="ResumeContent"/> (structured DateOnly periods, the
/// Resume's own <see cref="ResumeLanguage"/>) — the promoted Resume surface (TD-112 / #202).</item>
/// </list>
/// </summary>
public interface ICvRenderer
{
    ValueTask<RenderedCv> RenderAsync(
        ParsedResume parsedResume,
        RenderProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders a promoted Resume's already-decrypted <paramref name="content"/> (the Master
    /// version content, owner-resolved + DEK-decrypted by the read-handler) in
    /// <paramref name="language"/>. Mirrors the parsed overload: same <c>CvDocumentModel</c>
    /// projection + composer, only the source shape differs. The promoted content stores
    /// structured <c>DateOnly</c> periods (formatted to a display string for the PDF) and has no
    /// languages section (rendered as an honest partial — never a placeholder, §5).
    /// </summary>
    ValueTask<RenderedCv> RenderAsync(
        ResumeContent content,
        ResumeLanguage language,
        RenderProfile profile,
        CancellationToken cancellationToken);
}
