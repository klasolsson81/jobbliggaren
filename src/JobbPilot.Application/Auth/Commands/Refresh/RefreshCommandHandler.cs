using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Refresh;

public sealed class RefreshCommandHandler(
    IRefreshTokenStore refreshTokenStore,
    IUserAccountService userAccountService,
    IJwtTokenGenerator tokenGenerator)
    : ICommandHandler<RefreshCommand, Result<AuthTokensDto>>
{
    private static readonly DomainError InvalidToken =
        DomainError.Validation("Auth.InvalidRefreshToken", "Ogiltig eller utgången refresh token.");

    public async ValueTask<Result<AuthTokensDto>> Handle(
        RefreshCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Result.Failure<AuthTokensDto>(InvalidToken);

        var tokenHash = tokenGenerator.HashToken(command.RefreshToken);
        var stored = await refreshTokenStore.FindActiveByHashAsync(tokenHash, cancellationToken);

        if (stored is null)
            return Result.Failure<AuthTokensDto>(InvalidToken);

        var roles = await userAccountService.GetRolesAsync(stored.UserId, cancellationToken);
        var newTokens = tokenGenerator.GenerateTokens(stored.UserId, string.Empty, roles);

        var newHash = tokenGenerator.HashToken(newTokens.RefreshToken);

        await refreshTokenStore.StoreAsync(
            stored.UserId,
            newHash,
            newTokens.RefreshTokenExpiresAt,
            createdByIp: null,
            cancellationToken);

        await refreshTokenStore.RevokeAsync(stored.Id, replacedByTokenId: null, cancellationToken);

        return Result.Success(new AuthTokensDto(
            newTokens.AccessToken,
            newTokens.AccessTokenExpiresAt,
            newTokens.RefreshToken,
            newTokens.RefreshTokenExpiresAt));
    }
}
