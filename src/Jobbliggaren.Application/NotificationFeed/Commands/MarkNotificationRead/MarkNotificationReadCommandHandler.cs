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

        await NotificationFeedSources.MarkerFor(command.Kind).MarkRowAsync(
            new NotificationFeedMarkContext(db, currentUser.UserId.Value),
            new JobAdId(command.JobAdId),
            cancellationToken);

        return Result.Success();
    }
}
