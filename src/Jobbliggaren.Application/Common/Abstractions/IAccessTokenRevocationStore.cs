namespace Jobbliggaren.Application.Common.Abstractions;

[Obsolete(
    "JWT-revokering ersätts av ISessionStore.InvalidateAsync i Fas 0 STEG 4b (ADR 0017). " +
    "Interfacet bevaras tillfälligt för RefreshCommandHandler. Raderas i Fas 1.",
    error: false,
    DiagnosticId = "JOBBLIGGAREN0001",
    UrlFormat = "https://github.com/klasolsson81/jobbliggaren/blob/main/docs/decisions/0017-frontend-auth-pattern.md")]
public interface IAccessTokenRevocationStore
{
    Task RevokeAsync(string jti, TimeSpan expiresIn, CancellationToken ct);
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct);
}
