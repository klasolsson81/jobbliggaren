using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

public sealed class DeleteAccountCommandHandler(AccountDeletionScheduler scheduler, ICurrentUser currentUser)
    : ICommandHandler<DeleteAccountCommand, Result<AccountDeletionScheduled>>
{
    public async ValueTask<Result<AccountDeletionScheduled>> Handle(
        DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<AccountDeletionScheduled>(DomainError.Validation(
                AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att radera konto."));

        return await scheduler.ScheduleAsync(userId, false, cancellationToken);
    }
}
