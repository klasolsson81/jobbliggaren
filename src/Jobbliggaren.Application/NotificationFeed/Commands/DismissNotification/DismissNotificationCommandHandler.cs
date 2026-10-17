using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.DismissNotification;

public sealed class DismissNotificationCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<DismissNotificationCommand, Result>
{
    public async ValueTask<Result> Handle(DismissNotificationCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("NotificationFeed.Unauthorized", "Användaren är inte autentiserad."));

        // The ad is the notification (#2158): every source's rows of it, each source under its own gate.
        var context = new NotificationFeedMarkContext(db, currentUser.UserId.Value);
        var jobAdId = new JobAdId(command.JobAdId);
        foreach (var marker in NotificationFeedSources.Markers)
            await marker.DismissRowAsync(context, jobAdId, cancellationToken);

        return Result.Success();
    }
}
