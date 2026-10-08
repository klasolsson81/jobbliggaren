using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;

public sealed class ScheduleAccountDeletionCommandHandler(AccountDeletionScheduler scheduler, ICurrentUser currentUser)
    : ICommandHandler<ScheduleAccountDeletionCommand, Result<AccountDeletionScheduled>>
{
    public async ValueTask<Result<AccountDeletionScheduled>> Handle(
        ScheduleAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
            return Result.Failure<AccountDeletionScheduled>(DomainError.Validation(
                Auth.AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att radera konto."));
        if (actorId == command.UserId)
            return Result.Failure<AccountDeletionScheduled>(DomainError.Conflict(
                AccountAccessErrors.SelfDeletion, "Du kan inte schemalägga radering av ditt eget konto här."));

        return await scheduler.ScheduleAsync(command.UserId, true, cancellationToken);
    }
}
