using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Register;

public sealed class RegisterCommandHandler(
    IAppDbContext db,
    IUserAccountService userAccountService,
    IJwtTokenGenerator tokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IDateTimeProvider clock)
    : ICommandHandler<RegisterCommand, Result<AuthTokensDto>>
{
    public async ValueTask<Result<AuthTokensDto>> Handle(
        RegisterCommand command, CancellationToken cancellationToken)
    {
        var createResult = await userAccountService.CreateUserAsync(
            command.Email!, command.Password!, cancellationToken);

        if (createResult.IsFailure)
            return Result.Failure<AuthTokensDto>(createResult.Error);

        var userId = createResult.Value;

        var seekerResult = JobSeeker.Register(userId, command.DisplayName, clock);
        if (seekerResult.IsFailure)
        {
            await userAccountService.DeleteUserAsync(userId, cancellationToken);
            return Result.Failure<AuthTokensDto>(seekerResult.Error);
        }

        db.JobSeekers.Add(seekerResult.Value);

        var roles = await userAccountService.GetRolesAsync(userId, cancellationToken);
        var tokens = tokenGenerator.GenerateTokens(userId, command.Email!, roles);

        var tokenHash = tokenGenerator.HashToken(tokens.RefreshToken);
        await refreshTokenStore.StoreAsync(
            userId,
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
