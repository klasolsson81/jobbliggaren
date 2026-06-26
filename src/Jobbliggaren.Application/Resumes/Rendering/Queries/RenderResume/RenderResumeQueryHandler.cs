using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;

/// <summary>
/// Loads the OWNING job seeker's promoted Resume (its Master version content decrypted inside the
/// warmed field-encryption pipeline — Invariant 3) and renders it to a PDF. Mirrors
/// <c>RenderCvQueryHandler</c> + <c>GetResumeByIdQueryHandler</c>: resolve owner from
/// <see cref="ICurrentUser"/>, FirstOrDefault on <c>db.Resumes</c> (Versions included) filtered by
/// Id + JobSeekerId, return null on not-found OR cross-user (logging the cross-user attempt), else
/// render the Master content + the Resume's language and map to the shared transport DTO. The
/// renderer takes the decrypted content — this handler is the only thing that touches the
/// DbContext + the DEK pipeline. The bytes are streamed, never persisted.
/// </summary>
public sealed class RenderResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvRenderer renderer,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<RenderResumeQuery, RenderedCvDto?>
{
    public async ValueTask<RenderedCvDto?> Handle(
        RenderResumeQuery query, CancellationToken cancellationToken)
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
                    "Resume", resumeId.Value, currentUser.UserId.Value, "RenderResume");
            }
            return null;
        }

        // The validator guarantees a parseable RenderProfile (fail-loud, case-sensitive).
        var profile = Enum.Parse<RenderProfile>(query.Profile);
        var rendered = await renderer.RenderAsync(
            resume.MasterVersion.Content, resume.Language, profile, cancellationToken);
        return new RenderedCvDto(
            rendered.PdfBytes, rendered.ContentType, rendered.Profile.ToString(), rendered.Language.ToString());
    }
}
