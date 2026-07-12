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
    /// <summary>
    /// Renders a parsed staging CV. <paramref name="options"/> selects the visual template + accent
    /// / font / density (PR-8b); a parsed CV has no stored options, so the caller passes
    /// <see cref="CvTemplateOptions.Default"/> (Klar) — styling is a promoted-Resume concern.
    /// </summary>
    ValueTask<RenderedCv> RenderAsync(
        ParsedResume parsedResume,
        CvTemplateOptions options,
        RenderProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders a promoted Resume's already-decrypted <paramref name="content"/> (the Master
    /// version content, owner-resolved + DEK-decrypted by the read-handler) in
    /// <paramref name="language"/> using the CV's <paramref name="options"/> (visual template + accent
    /// / font / density, PR-8b). Mirrors the parsed overload: same <c>CvDocumentModel</c> projection +
    /// composer, only the source shape differs. The <see cref="RenderProfile.Ats"/> profile always
    /// renders the plain single-column ATS parallel from the SAME content, ignoring the visual template
    /// (design handoff §5.5/§8) — so a two-column choice never costs the user a parseable version.
    /// </summary>
    ValueTask<RenderedCv> RenderAsync(
        ResumeContent content,
        ResumeLanguage language,
        CvTemplateOptions options,
        RenderProfile profile,
        CancellationToken cancellationToken);
}
