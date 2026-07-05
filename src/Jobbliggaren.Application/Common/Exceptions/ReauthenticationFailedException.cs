namespace Jobbliggaren.Application.Common.Exceptions;

/// <summary>
/// Thrown by <c>ReauthenticationBehavior</c> when an <c>IReauthenticatingRequest</c>'s password
/// does not validate — wrong password, a locked account, OR a soft-deleted account, all
/// indistinguishable. Mapped centrally in the Api (<c>Program.cs</c>) to a byte-identical
/// <c>Auth.InvalidCredentials</c> 401 (via <c>AuthProblem.InvalidCredentials()</c>) so re-auth
/// failure never leaks which cause applied (GDPR Art. 32 oracle-avoidance). The message carries
/// NO credential material (it is internal only — the client sees the central ProblemDetails).
/// </summary>
public sealed class ReauthenticationFailedException()
    : Exception("Re-autentisering misslyckades.");
