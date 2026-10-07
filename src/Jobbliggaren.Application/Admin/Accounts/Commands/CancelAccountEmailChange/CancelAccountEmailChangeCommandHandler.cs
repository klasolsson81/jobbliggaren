using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;

/// <summary>
/// One guarded removal in the store. Against a completion racing it, the store lets exactly one of the two through, so
/// a cancel that answers success means the change can no longer complete.
/// </summary>
public sealed class CancelAccountEmailChangeCommandHandler(IAccountEmailChangeStore store)
    : ICommandHandler<CancelAccountEmailChangeCommand, Result>
{
    public async ValueTask<Result> Handle(CancelAccountEmailChangeCommand command, CancellationToken cancellationToken) =>
        await store.CancelAsync(command.UserId, cancellationToken)
            ? Result.Success()
            : Result.Failure(DomainError.Gone(
                AuthErrorCodes.AccountEmailChangeNothingPending, AuthErrorCodes.AccountEmailChangeNothingPendingMessage));
}
