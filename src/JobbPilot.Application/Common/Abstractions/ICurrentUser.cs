namespace JobbPilot.Application.Common.Abstractions;

/// <summary>
/// Stub i STEG 2 — riktig implementation kommer i STEG 3 efter auth-ADR.
/// Authorization behavior använder denna för att avgöra access.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    string? Jti { get; }
}
