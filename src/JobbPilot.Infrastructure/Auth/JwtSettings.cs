namespace JobbPilot.Infrastructure.Auth;

[Obsolete(
    "JwtSettings (Infrastructure) är inte längre nödvändig sedan JWT-autentisering ersattes av ISessionStore i STEG 4b (ADR 0017). " +
    "Raderas i Fas 1.",
    error: false,
    DiagnosticId = "JOBBPILOT0001",
    UrlFormat = "https://github.com/klasolsson81/jobbpilot/blob/main/docs/decisions/0017-frontend-auth-pattern.md")]
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; init; } = 15;
    public int RefreshTokenLifetimeDays { get; init; } = 14;
    public string PrivateKeyPath { get; init; } = string.Empty;
    public string PublicKeyPath { get; init; } = string.Empty;
}
