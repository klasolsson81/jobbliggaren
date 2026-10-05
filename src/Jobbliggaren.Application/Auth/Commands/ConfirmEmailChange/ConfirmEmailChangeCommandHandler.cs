using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

/// <summary>
/// #1739 — the change-email confirm step (ADR 0142 D5): the grant is redeemed for this user and this address, the
/// store asserting both, and only then is the account moved. The old address is told after a successful swap.
/// </summary>
public sealed class ConfirmEmailChangeCommandHandler(
    ICurrentUser currentUser,
    IGrantStore grants,
    ConfirmedAddressSwap addressSwap)
    : ICommandHandler<ConfirmEmailChangeCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att byta e-postadress."));

        // The validator guarantees both are non-empty; re-assert so the handler is correct in isolation.
        if (string.IsNullOrEmpty(command.ChangeEmailGrant) || string.IsNullOrEmpty(command.NewEmail))
            return Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.InvalidInput, "Ny e-postadress krävs."));

        var userId = currentUser.UserId.Value;

        // The store asserts the user AND the address (ADR 0142 D3): a grant for another user, another address,
        // another purpose, expired or already used is one answer. The redemption is single use either way.
        var subject = await grants.RedeemAsync(
            GrantToken.FromRaw(command.ChangeEmailGrant),
            GrantAssertion.Of(new GrantSubject.ChangeEmail(userId, command.NewEmail)),
            cancellationToken);
        if (subject is null)
            return Result.Failure<Guid>(DomainError.Gone(
                AuthErrorCodes.EmailChangeGrantUnusable, AuthErrorCodes.EmailChangeGrantUnusableMessage));

        var moved = await addressSwap.MoveAsync(userId, command.NewEmail, SwapPrecondition.None, cancellationToken);
        if (moved.IsFailure)
            return Result.Failure<Guid>(moved.Error);

        // The User.EmailChanged audit aggregate id AND the id the endpoint re-issues the session for.
        return Result.Success(userId);
    }
}
