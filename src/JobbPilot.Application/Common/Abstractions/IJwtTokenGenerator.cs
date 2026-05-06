namespace JobbPilot.Application.Common.Abstractions;

public sealed record GeneratedTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

[Obsolete(
    "JWT-issuance ersätts av ISessionStore i Fas 0 STEG 4b (ADR 0017). " +
    "Interfacet bevaras tillfälligt för RefreshCommandHandler. Raderas i Fas 1.",
    error: false,
    DiagnosticId = "JOBBPILOT0001",
    UrlFormat = "https://github.com/klasolsson81/jobbpilot/blob/main/docs/decisions/0017-frontend-auth-pattern.md")]
public interface IJwtTokenGenerator
{
    GeneratedTokens GenerateTokens(Guid userId, string email, IEnumerable<string> roles);
    string HashToken(string rawToken);
}
