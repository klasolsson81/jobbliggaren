using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;

public sealed class UpdateMyProfileCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<UpdateMyProfileCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId!.Value, cancellationToken)
            ?? throw new NotFoundException($"{nameof(JobSeeker)} hittades inte för användare {currentUser.UserId!.Value}.");

        if (command.DisplayName is not null)
        {
            var nameResult = jobSeeker.UpdateDisplayName(command.DisplayName, clock);
            if (nameResult.IsFailure)
                return nameResult;
        }

        if (command.Language is not null || command.EmailNotifications.HasValue || command.WeeklySummary.HasValue)
        {
            var prefs = new Preferences(
                Language: command.Language ?? jobSeeker.Preferences.Language,
                EmailNotifications: command.EmailNotifications ?? jobSeeker.Preferences.EmailNotifications,
                WeeklySummary: command.WeeklySummary ?? jobSeeker.Preferences.WeeklySummary);
            jobSeeker.UpdatePreferences(prefs, clock);
        }

        return Result.Success();
    }
}
