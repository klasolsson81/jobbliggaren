using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Configuration;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace JobbPilot.Application.Auth.Commands.Logout;

public sealed class LogoutCommandHandler(
    ICurrentUser currentUser,
    IRefreshTokenStore refreshTokenStore,
    IAccessTokenRevocationStore revocationStore,
    IOptions<JwtSettings> jwtSettings)
    : ICommandHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(
        LogoutCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.Jti is not null)
            await revocationStore.RevokeAsync(
                currentUser.Jti,
                TimeSpan.FromMinutes(jwtSettings.Value.AccessTokenLifetimeMinutes),
                cancellationToken);

        if (currentUser.UserId.HasValue)
            await refreshTokenStore.RevokeAllForUserAsync(
                currentUser.UserId.Value, cancellationToken);

        return Result.Success();
    }
}
