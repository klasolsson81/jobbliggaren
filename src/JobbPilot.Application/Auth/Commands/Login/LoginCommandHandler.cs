using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler(
    IUserAccountService userAccountService,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger)
    : ICommandHandler<LoginCommand, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(
        LoginCommand command, CancellationToken cancellationToken)
    {
        var credentialsResult = await userAccountService.ValidateCredentialsAsync(
            command.Email!, command.Password!, cancellationToken);

        if (credentialsResult.IsFailure)
        {
            auditLogger.LoginFailed(HashEmail(command.Email!));
            return Result.Failure<SessionDto>(credentialsResult.Error);
        }

        var userId = credentialsResult.Value.UserId;
        var session = await sessionStore.CreateAsync(userId, cancellationToken);

        auditLogger.LoginSucceeded(userId, session.Id.ToString());

        return Result.Success(new SessionDto(session.Id.Reveal()));
    }

    private static string HashEmail(string email)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
