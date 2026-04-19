namespace JobbPilot.Application.Common.Abstractions;

/// <summary>
/// Markerinterface — commands/queries som implementerar detta kräver att
/// användaren är autentiserad. Kontrolleras av AuthorizationBehavior.
/// </summary>
public interface IAuthenticatedRequest;
