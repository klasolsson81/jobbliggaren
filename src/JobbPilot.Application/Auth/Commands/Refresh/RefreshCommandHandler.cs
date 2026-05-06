using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.Auth.Commands.Refresh;

// NOTE: Denna handler är inte längre wired upp sedan Turn 4.
// /auth/refresh returnerar 410 Gone direkt (AuthEndpoints.cs).
// Handler + tester bevaras som referens tills RefreshTokenStore-tabellen
// migreras bort i Fas 1 (ADR 0017).
#pragma warning disable JOBBPILOT0001 // JWT refresh bevaras tills Fas 1, ADR 0017
public sealed partial class RefreshCommandHandler(
    IRefreshTokenStore refreshTokenStore,
    IUserAccountService userAccountService,
    IJwtTokenGenerator tokenGenerator,
    IDateTimeProvider clock,
    ILogger<RefreshCommandHandler> logger)
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
        var stored = await refreshTokenStore.FindByHashAsync(tokenHash, cancellationToken);

        if (stored is null)
            return Result.Failure<AuthTokensDto>(InvalidToken);

        // Replay-detektering per ADR 0014: redan roterad token → revokera hela kedjan
        if (stored.RevokedAt is not null)
        {
            LogReplayDetected(stored.UserId);
            await refreshTokenStore.RevokeAllForUserAsync(stored.UserId, cancellationToken);
            return Result.Failure<AuthTokensDto>(InvalidToken);
        }

        if (stored.ExpiresAt <= clock.UtcNow)
            return Result.Failure<AuthTokensDto>(InvalidToken);

        var roles = await userAccountService.GetRolesAsync(stored.UserId, cancellationToken);
        var email = await userAccountService.GetEmailAsync(stored.UserId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"User {stored.UserId} har refresh token men ingen email — data-integritetsfel.");

        var newTokens = tokenGenerator.GenerateTokens(stored.UserId, email, roles);

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Refresh token replay detected for user {UserId}. Revoking entire chain.")]
    private partial void LogReplayDetected(Guid userId);
}
#pragma warning restore JOBBPILOT0001
