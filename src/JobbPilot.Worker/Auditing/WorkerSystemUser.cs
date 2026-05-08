using JobbPilot.Application.Common.Abstractions;

namespace JobbPilot.Worker.Auditing;

/// <summary>
/// Stub-implementation av <see cref="ICurrentUser"/> för Worker-context.
/// Worker-jobb kör utan inloggad användare — system-jobb. Audit-rader för
/// markerade <c>IAuditableCommand</c>:s får <c>user_id = NULL</c> per ADR 0022.
///
/// Singleton-livstid: konstant state (inga fält muteras). Sparar allokationer
/// vid jobb-execution.
///
/// <c>AuthorizationBehavior</c> i Worker-pipelinen släpper igenom commands som
/// inte implementerar <c>IAuthenticatedRequest</c> (t.ex. <c>MarkGhostedCommand</c>).
/// Commands som kräver autentisering ska aldrig dispatchas från Worker.
/// </summary>
public sealed class WorkerSystemUser : ICurrentUser
{
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
    public string? Jti => null;
    public string? Email => null;
    public SessionId? SessionId => null;
}
