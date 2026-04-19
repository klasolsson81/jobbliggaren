using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler(
    IUserAccountService userAccountService,
    IJwtTokenGenerator tokenGenerator,
    IRefreshTokenStore refreshTokenStore)
    : ICommandHandler<LoginCommand, Result<AuthTokensDto>>
{
    public async ValueTask<Result<AuthTokensDto>> Handle(
        LoginCommand command, CancellationToken cancellationToken)
    {
        var credentialsResult = await userAccountService.ValidateCredentialsAsync(
            command.Email!, command.Password!, cancellationToken);

        if (credentialsResult.IsFailure)
            return Result.Failure<AuthTokensDto>(credentialsResult.Error);

        var credentials = credentialsResult.Value;
        var tokens = tokenGenerator.GenerateTokens(
            credentials.UserId, command.Email!, credentials.Roles);

        var tokenHash = tokenGenerator.HashToken(tokens.RefreshToken);
        await refreshTokenStore.StoreAsync(
            credentials.UserId,
            tokenHash,
            tokens.RefreshTokenExpiresAt,
            createdByIp: null,
            cancellationToken);

        return Result.Success(new AuthTokensDto(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt));
    }
}
