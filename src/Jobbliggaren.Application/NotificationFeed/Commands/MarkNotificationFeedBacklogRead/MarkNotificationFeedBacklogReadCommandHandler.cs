using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationFeedBacklogRead;

/// <summary>
/// #2130 — hands the backlog mark to the source of the kind, which reads the type's own off-stamp and marks the rows
/// created after it. Set-based and committed on its own; it only READS the <c>JobSeeker</c>, so it is not a writer.
/// </summary>
public sealed class MarkNotificationFeedBacklogReadCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<MarkNotificationFeedBacklogReadCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkNotificationFeedBacklogReadCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("NotificationFeed.Unauthorized", "Användaren är inte autentiserad."));

        await NotificationFeedSources.MarkerFor(command.Kind).MarkBacklogAsync(
            new NotificationFeedMarkContext(db, currentUser.UserId.Value), cancellationToken);

        return Result.Success();
    }
}
