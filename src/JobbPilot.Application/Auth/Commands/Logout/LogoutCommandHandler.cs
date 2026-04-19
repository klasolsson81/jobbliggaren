using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Logout;

public sealed class LogoutCommandHandler(
    ICurrentUser currentUser,
    IRefreshTokenStore refreshTokenStore,
    IAccessTokenRevocationStore revocationStore)
    : ICommandHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(
        LogoutCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.Jti is not null)
            await revocationStore.RevokeAsync(
                currentUser.Jti,
                TimeSpan.FromMinutes(20),
                cancellationToken);

        if (currentUser.UserId.HasValue)
            await refreshTokenStore.RevokeAllForUserAsync(
                currentUser.UserId.Value, cancellationToken);

        return Result.Success();
    }
}
