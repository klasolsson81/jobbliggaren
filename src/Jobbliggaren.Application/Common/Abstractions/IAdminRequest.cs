namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Marker-interface för commands/queries som kräver Admin-roll. Kontrolleras av
/// <see cref="Behaviors.AdminAuthorizationBehavior{TMessage,TResponse}"/> som
/// defense-in-depth bakom HTTP-lagrets <c>[Authorize(Policy = "Admin")]</c>.
///
/// Ärver <see cref="IAuthenticatedRequest"/> så att <see cref="Behaviors.AuthorizationBehavior{TMessage,TResponse}"/>
/// först fångar saknad autentisering med 401 innan roll-check körs (annars
/// skulle anonyma anrop få 403 där 401 är korrekt semantik).
/// </summary>
public interface IAdminRequest : IAuthenticatedRequest;
