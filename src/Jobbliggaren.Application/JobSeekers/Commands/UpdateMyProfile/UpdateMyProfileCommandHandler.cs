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

        if (command.Language is not null)
        {
            // Mutate ONLY the locale via `with` — preserving every Vag 4 consent field
            // (BackgroundMatchNotificationsEnabled, DigestCadence, the Art. 7 consent
            // timestamps). The previous `new Preferences(Language, EmailNotifications,
            // WeeklySummary)` form silently reset those four fields to their defaults
            // (consent OFF, timestamps null) on any profile change — a latent GDPR
            // consent-clobber. Fixed in-block with the TD-115 retire (the legacy flags
            // are gone; locale is the only mutable field left on this command).
            jobSeeker.UpdatePreferences(
                jobSeeker.Preferences with { Language = command.Language }, clock);
        }

        return Result.Success();
    }
}
