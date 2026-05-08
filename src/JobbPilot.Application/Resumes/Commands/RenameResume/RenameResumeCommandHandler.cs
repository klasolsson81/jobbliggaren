using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Resumes.Commands.RenameResume;

public sealed class RenameResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<RenameResumeCommand, Result>
{
    public async ValueTask<Result> Handle(
        RenameResumeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(command.ResumeId);
        var resume = await db.Resumes
            .FirstOrDefaultAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (resume is null)
            throw new NotFoundException("CV hittades inte.");

        return resume.Rename(command.Name, clock);
    }
}
