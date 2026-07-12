using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResumePreview;

/// <summary>
/// Renders the OWNING job seeker's promoted Resume with UNSAVED template options — the mallbyggare's
/// ephemeral live-preview (Fas 4b PR-8b 8b.3, CTO-bind Q1 Variant B). Mirrors
/// <c>RenderResumeQueryHandler</c>'s owner-resolve → load (Versions) → cross-user → null + audit
/// orchestration, then composes the four requested visual options over the PERSISTED photo config
/// (the write-contract excludes photo — DPIA-gated to PR-10 — so the preview can never enable a
/// photo, fail-closed by construction) and renders <see cref="RenderProfile.Visual"/> ONLY (the Ats
/// profile ignores the template, so an ephemeral-Ats preview would present a knob that moves
/// nothing). Nothing is persisted — the composed options live only for the duration of this render,
/// and <c>db.Resumes</c> is read <c>AsNoTracking</c> (preview == export via the shared
/// <see cref="ICvRenderer"/>). The renderer is the only heavy dependency; this handler is the
/// ownership/cross-user orchestration + the ephemeral composition, not the QuestPDF internals.
/// </summary>
public sealed class RenderResumePreviewQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvRenderer renderer,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<RenderResumePreviewQuery, RenderedCvDto?>
{
    public async ValueTask<RenderedCvDto?> Handle(
        RenderResumePreviewQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var resumeId = new ResumeId(query.ResumeId);
        var resume = await db.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .Where(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "RenderResumePreview");
            }
            return null;
        }

        // Defense-in-depth: the validator already resolved these names, but a direct Send (test /
        // future caller) must degrade to null (→ 404), never an unmapped throw / a 500.
        if (!CvTemplate.TryFromName(query.Template, out var template)
            || !CvAccentColor.TryFromName(query.AccentColor, out var accent)
            || !CvFontPair.TryFromName(query.FontPair, out var fontPair)
            || !CvDensity.TryFromName(query.Density, out var density))
        {
            return null;
        }

        // Compose the requested visual options over the PERSISTED photo config: the write-contract
        // excludes photo (DPIA/PR-10), so the preview preserves whatever photo state is stored and
        // never enables one. record `with` keeps value semantics on the AsNoTracking-loaded VO —
        // a transient value handed to the renderer; nothing is attached or persisted.
        var options = resume.TemplateOptions with
        {
            Template = template,
            AccentColor = accent,
            FontPair = fontPair,
            Density = density,
        };

        // Visual only — the Ats profile ignores the template (the four options would move nothing).
        var rendered = await renderer.RenderAsync(
            resume.MasterVersion.Content, resume.Language, options, RenderProfile.Visual, cancellationToken);
        return new RenderedCvDto(
            rendered.PdfBytes, rendered.ContentType, rendered.Profile.ToString(), rendered.Language.ToString());
    }
}
