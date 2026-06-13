using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Logout;

public sealed class LogoutCommandHandler(
    ICurrentUser currentUser,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger)
    : ICommandHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(
        LogoutCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.SessionId is { } sessionId)
        {
            // Returvärde ignoreras — logout är idempotent (race: annan enhet loggade ut simultant)
            _ = await sessionStore.InvalidateAsync(sessionId, cancellationToken);

            if (currentUser.UserId is { } userId)
                auditLogger.LogoutSucceeded(userId, sessionId.ToString());
        }

        return Result.Success();
    }
}
