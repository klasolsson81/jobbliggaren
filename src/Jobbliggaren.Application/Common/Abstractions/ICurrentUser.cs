namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Authorization behavior använder denna för att avgöra access.
/// SessionId sätts av SessionAuthenticationHandler vid lyckad session-validering.
///
/// <para>
/// Exponerar ENDAST det den körande auth-scheman faktiskt kan leverera. Fram till #822
/// bar porten <c>Email</c> och <c>Jti</c> — båda lästa ur claims som bara den avvecklade
/// JWT-vägen (ADR 0017) emit:ade, så under opaka sessioner returnerade de ALLTID null.
/// En medlem som varje produktions-implementation resolvar till null är en trasig
/// kontrakt-lögn, inte en nullable bekvämlighet (ISP): <c>Email</c> konsumerades i god
/// tro av GetCurrentUserQueryHandler och dödade radera-konto-vägen (GDPR Art. 17).
/// Båda är borttagna. E-postadressen ägs av identity-storen — hämta den per userId via
/// <see cref="IUserAccountService.GetEmailAsync"/>.
/// </para>
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
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
