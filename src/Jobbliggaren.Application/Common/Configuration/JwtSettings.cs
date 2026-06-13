namespace Jobbliggaren.Application.Common.Configuration;

[Obsolete(
    "JwtSettings (Application) är inte längre i bruk sedan LogoutCommandHandler refaktorerades i STEG 4b (ADR 0017). " +
    "Raderas i Fas 1.",
    error: false,
    DiagnosticId = "JOBBLIGGAREN0001",
    UrlFormat = "https://github.com/klasolsson81/jobbliggaren/blob/main/docs/decisions/0017-frontend-auth-pattern.md")]
public sealed record JwtSettings
{
    public const string SectionName = "Jwt";

    public int AccessTokenLifetimeMinutes { get; init; } = 15;
}
