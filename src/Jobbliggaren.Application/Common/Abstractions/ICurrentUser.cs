namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Authorization behavior använder denna för att avgöra access.
/// SessionId sätts av SessionAuthenticationHandler vid lyckad session-validering.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    string? Jti { get; }
    string? Email { get; }
    SessionId? SessionId { get; }

    /// <summary>
    /// Kontrollerar om aktuell principal har rollen. Implementeras typiskt via
    /// <c>ClaimsPrincipal.IsInRole(role)</c>, som konsulterar
    /// <c>ClaimTypes.Role</c>-claims. Roller emit:as av
    /// SessionAuthenticationHandler per request (per-request fetch, ADR 0017
    /// + senior-cto-advisor-beslut 2026-05-11).
    /// </summary>
    bool IsInRole(string role);
}
