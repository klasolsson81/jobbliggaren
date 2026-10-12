using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationsReadThrough;

/// <summary>
/// #2130 — hands the bulk mark to the source of the kind. The write is set-based and commits on its own
/// (<c>ExecuteUpdate</c>), so this handler holds nothing for the unit of work to save and touches no <c>JobSeeker</c>.
/// </summary>
public sealed class MarkNotificationsReadThroughCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<MarkNotificationsReadThroughCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkNotificationsReadThroughCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("NotificationFeed.Unauthorized", "Användaren är inte autentiserad."));

        await NotificationFeedSources.MarkerFor(command.Kind).MarkThroughAsync(
            new NotificationFeedMarkContext(db, currentUser.UserId.Value), command.Through, cancellationToken);

        return Result.Success();
    }
}
