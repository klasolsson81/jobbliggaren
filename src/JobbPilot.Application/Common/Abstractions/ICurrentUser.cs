namespace JobbPilot.Application.Common.Abstractions;

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
}
