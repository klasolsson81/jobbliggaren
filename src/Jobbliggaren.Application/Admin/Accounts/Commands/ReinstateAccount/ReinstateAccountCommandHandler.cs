using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;

public sealed class ReinstateAccountCommandHandler(ICurrentUser currentUser, IAccountAccessWriter access)
    : ICommandHandler<ReinstateAccountCommand, Result<AccountAccessChanged>>
{
    public async ValueTask<Result<AccountAccessChanged>> Handle(
        ReinstateAccountCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
            return Result.Failure<AccountAccessChanged>(DomainError.Validation(
                AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att ändra kontoåtkomst."));
        return await access.ChangeAsync(actorId, command.UserId, false, cancellationToken);
    }
}
