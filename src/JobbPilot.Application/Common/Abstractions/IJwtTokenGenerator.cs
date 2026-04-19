namespace JobbPilot.Application.Common.Abstractions;

public sealed record GeneratedTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

public interface IJwtTokenGenerator
{
    GeneratedTokens GenerateTokens(Guid userId, string email, IEnumerable<string> roles);
    string HashToken(string rawToken);
}
