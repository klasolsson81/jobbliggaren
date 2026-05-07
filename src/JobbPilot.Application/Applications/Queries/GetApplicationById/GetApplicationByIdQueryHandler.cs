using JobbPilot.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Queries.GetApplicationById;

public sealed class GetApplicationByIdQueryHandler(IAppDbContext db, ICurrentUser currentUser)
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

        var app = await db.Applications
            .AsNoTracking()
            .Include(a => a.FollowUps)
            .Include(a => a.Notes)
            .Where(a => a.Id == new JobbPilot.Domain.Applications.ApplicationId(query.Id) &&
                        a.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (app is null)
            return null;

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
