using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService)
    : ICommandHandler<ChangePasswordCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        // Self-defending (mirrors DeleteAccountCommandHandler): AuthorizationBehavior and
        // ReauthenticationBehavior run before this handler, but we do not take a dependency on
        // pipeline configuration. Throw-safe fallbacks instead of the null-forgiving operator.
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.NotAuthenticated", "Inloggning krävs för att byta lösenord."));

        // The validator guarantees both are non-empty; re-assert so the handler is correct in
        // isolation and the non-null values can be passed to the Identity port.
        if (string.IsNullOrEmpty(command.CurrentPassword) || string.IsNullOrEmpty(command.NewPassword))
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.InvalidInput", "Nuvarande och nytt lösenord krävs."));

        var userId = currentUser.UserId.Value;

        // The current password was already verified by ReauthenticationBehavior; UserManager
        // re-verifies it atomically as part of the change and re-stamps the security stamp. Note the
        // stamp rotation does NOT invalidate the Redis-backed sessions — the endpoint's C6 re-issue
        // is the only logout-everywhere mechanism. IdentityError -> DomainError mapping matches
        // CreateUserAsync (e.g. Auth.PasswordTooShort / Auth.PasswordMismatch).
        var result = await userAccountService.ChangePasswordAsync(
            userId, command.CurrentPassword, command.NewPassword, cancellationToken);

        // Return the authenticated user id: the AuditBehavior aggregate id (User.PasswordChanged)
        // AND the id the endpoint re-issues the session for. AuditBehavior skips failures, so a
        // failed change writes no audit row.
        return result.IsFailure
            ? Result.Failure<Guid>(result.Error)
            : Result.Success(userId);
    }
}
