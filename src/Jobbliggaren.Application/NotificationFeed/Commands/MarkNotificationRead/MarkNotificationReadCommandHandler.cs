using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;

public sealed class MarkNotificationReadCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<MarkNotificationReadCommand, Result>
{
    public async ValueTask<Result> Handle(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("NotificationFeed.Unauthorized", "Användaren är inte autentiserad."));

        // The ad is the notification (#2158): every source's rows of it, each source under its own gate.
        var context = new NotificationFeedMarkContext(db, currentUser.UserId.Value);
        var jobAdId = new JobAdId(command.JobAdId);
        foreach (var marker in NotificationFeedSources.Markers)
            await marker.MarkRowAsync(context, jobAdId, cancellationToken);

        return Result.Success();
    }
}
