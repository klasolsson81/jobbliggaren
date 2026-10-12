using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;

/// <summary>
/// #2130 — hands the click to the source of the row's kind (<see cref="NotificationFeedSources.MarkerFor"/>), which
/// applies its own gate and loads the owner's rows tracked, so <c>UnitOfWorkBehavior</c> persists the flag. The
/// handler is generator-registered and so public; the markers are internal and reached through the static registry,
/// the way the feed handler reaches the sources.
/// </summary>
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
