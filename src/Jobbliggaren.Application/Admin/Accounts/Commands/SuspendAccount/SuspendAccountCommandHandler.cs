using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;

public sealed class SuspendAccountCommandHandler(ICurrentUser currentUser, IAccountAccessWriter access)
    : ICommandHandler<SuspendAccountCommand, Result<AccountAccessChanged>>
{
    public async ValueTask<Result<AccountAccessChanged>> Handle(
        SuspendAccountCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
            return Result.Failure<AccountAccessChanged>(DomainError.Validation(
                AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att ändra kontoåtkomst."));
        return await access.ChangeAsync(actorId, command.UserId, true, cancellationToken);
    }
}
