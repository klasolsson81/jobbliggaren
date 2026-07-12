namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Authorization behavior använder denna för att avgöra access.
/// SessionId sätts av SessionAuthenticationHandler vid lyckad session-validering.
///
/// <para>
/// Exponerar ENDAST det den körande auth-scheman faktiskt kan leverera. Ett
/// <c>Email</c>-medlem fanns här fram till #822: det lästes ur en e-post-claim som
/// bara den avvecklade JWT-vägen (ADR 0017) emit:ade, så under opaka sessioner
/// returnerade det alltid null — en trasig kontrakt-lögn snarare än en nullable
/// bekvämlighet (ISP). E-postadressen ägs av identity-storen; hämta den via
/// <see cref="IUserAccountService.GetEmailAsync"/> per userId.
/// </para>
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    string? Jti { get; }
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
