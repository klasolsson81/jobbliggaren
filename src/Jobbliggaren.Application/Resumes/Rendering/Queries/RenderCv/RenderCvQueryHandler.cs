using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;

/// <summary>
/// Loads the OWNING job seeker's parsed CV (decrypted inside the warmed field-encryption
/// pipeline — Invariant 3) and renders it to a PDF. Mirrors
/// <c>ReviewParsedResumeQueryHandler</c>: resolve owner from <see cref="ICurrentUser"/>,
/// FirstOrDefault on <c>db.ParsedResumes</c> filtered by Id + JobSeekerId, return null on
/// not-found OR cross-user (logging the cross-user attempt), else render and map to the
/// transport DTO. The renderer takes the decrypted aggregate — this handler is the only thing
/// that touches the DbContext + the DEK pipeline. The bytes are streamed, never persisted.
/// </summary>
public sealed class RenderCvQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvRenderer renderer,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<RenderCvQuery, RenderedCvDto?>
{
    public async ValueTask<RenderedCvDto?> Handle(
        RenderCvQuery query, CancellationToken cancellationToken)
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

        var parsedResumeId = new ParsedResumeId(query.ParsedResumeId);
        var resume = await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.Id == parsedResumeId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "RenderCv");
            }
            return null;
        }

        // The validator guarantees a parseable RenderProfile (fail-loud, case-sensitive).
        var profile = Enum.Parse<RenderProfile>(query.Profile);
        var rendered = await renderer.RenderAsync(resume, profile, cancellationToken);
        return new RenderedCvDto(
            rendered.PdfBytes, rendered.ContentType, rendered.Profile.ToString(), rendered.Language.ToString());
    }
}
