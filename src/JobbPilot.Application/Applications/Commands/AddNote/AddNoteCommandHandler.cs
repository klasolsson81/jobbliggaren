using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Commands.AddNote;

public sealed class AddNoteCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<AddNoteCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        AddNoteCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var appId = new JobbPilot.Domain.Applications.ApplicationId(command.ApplicationId);
        var app = await db.Applications
            .Include(a => a.Notes)
            .FirstOrDefaultAsync(a => a.Id == appId && a.JobSeekerId == jobSeekerId, cancellationToken);

        if (app is null)
            throw new NotFoundException("Ansökan hittades inte.");

        var result = app.AddNote(command.Content, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        var addedNote = app.Notes[^1];
        return Result.Success(addedNote.Id.Value);
    }
}
