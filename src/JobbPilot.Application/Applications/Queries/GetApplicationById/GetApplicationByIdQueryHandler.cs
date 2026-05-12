using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Queries.GetApplicationById;

public sealed class GetApplicationByIdQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetApplicationByIdQuery, ApplicationDetailDto?>
{
    public async ValueTask<ApplicationDetailDto?> Handle(
        GetApplicationByIdQuery query, CancellationToken cancellationToken)
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

        var applicationId = new JobbPilot.Domain.Applications.ApplicationId(query.Id);

        var app = await db.Applications
            .AsNoTracking()
            .Include(a => a.FollowUps)
            .Include(a => a.Notes)
            .Where(a => a.Id == applicationId && a.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (app is null)
        {
            // Failed-access-detection (ADR 0031 / TD-67): skilj "okänt id" från
            // "tillhör annan user" för anomaly-loggning. Klient ser identisk 404.
            var exists = await db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.Id == applicationId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Application", applicationId.Value, currentUser.UserId.Value,
                    "GetApplicationById");
            }
            return null;
        }

        return new ApplicationDetailDto(
            app.Id.Value,
            app.JobSeekerId.Value,
            app.JobAdId == null ? (Guid?)null : app.JobAdId.Value.Value,
            app.Status.Name,
            app.CoverLetter,
            app.CreatedAt,
            app.UpdatedAt,
            app.FollowUps.Select(f => new FollowUpDto(
                f.Id.Value,
                f.Channel.Name,
                f.ScheduledAt,
                f.Note,
                f.Outcome.Name,
                f.OutcomeAt,
                f.CreatedAt)).ToList(),
            app.Notes.Select(n => new NoteDto(
                n.Id.Value,
                n.Content,
                n.CreatedAt)).ToList());
    }
}
