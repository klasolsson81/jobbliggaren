using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetResumeAtsText;

/// <summary>
/// Loads the OWNING job seeker's canonical Resume (master content decrypted inside the
/// warmed field-encryption pipeline), linearizes it via the shared linearizer (ADR 0093
/// §D8 SPOT — the exact text the review cites and the ATS-PDF renders) and returns it
/// with the "Linearized" source claim. Mirrors <c>ReviewResumeQueryHandler</c>:
/// owner-resolve, FirstOrDefault by Id + JobSeekerId, cross-user attempt logged, null on
/// not-found. The text passes <c>PersonnummerRedactor</c> before egress — belt-and-braces
/// (every canonical write path is already pnr-guarded), same defense-in-depth posture as
/// the review evidence path.
/// </summary>
public sealed class GetResumeAtsTextQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetResumeAtsTextQuery, ResumeAtsTextDto?>
{
    /// <summary>The only source claim this endpoint emits (CTO-bind Q3, D5e).</summary>
    internal const string LinearizedSource = "Linearized";

    public async ValueTask<ResumeAtsTextDto?> Handle(
        GetResumeAtsTextQuery query, CancellationToken cancellationToken)
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
                    "Resume", resumeId.Value, currentUser.UserId.Value, "GetResumeAtsText");
            }
            return null;
        }

        var linearized = ResumeContentLinearizer.Linearize(resume.MasterVersion.Content);

        // Belt-and-braces (§5 pnr-guard highest priority): canonical content is
        // pnr-clean by every write path's shared guard, but a NEW egress surface never
        // relies on that alone — same posture as the engine's evidence redaction.
        var text = PersonnummerRedactor.Redact(linearized.Text);

        return new ResumeAtsTextDto(LinearizedSource, text);
    }
}
