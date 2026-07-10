using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyEmail;

public sealed class VerifyEmailCommandHandler(IUserAccountService userAccountService)
    : ICommandHandler<VerifyEmailCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        // Public / token-gated (no ICurrentUser): the URL-safe token IS the authorization. The
        // validator guarantees non-empty Uid/Token; re-assert so the handler is correct in isolation.
        if (command.Uid == Guid.Empty || string.IsNullOrEmpty(command.Token))
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.InvalidInput", "Ogiltig bekräftelselänk."));

        // Verify the token and set EmailConfirmed=true. ONE uniform failure for every rejection
        // (user-not-found / bad-or-expired / malformed token) so this public endpoint reveals no
        // account-existence oracle. No logout-everywhere (this is not a recovery-vector change — the
        // address was always the account's) and no session is issued (the user then logs in).
        var result = await userAccountService.ConfirmEmailAsync(
            command.Uid, command.Token, cancellationToken);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        return Result.Success(command.Uid);
    }
}
