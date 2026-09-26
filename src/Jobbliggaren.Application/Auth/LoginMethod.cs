namespace Jobbliggaren.Application.Auth;

/// <summary>How a session was earned: the login-succeeded audit event records it (#1735).</summary>
public enum LoginMethod
{
    Code = 1,
    Link = 2,

    /// <summary>A Google login (#1744, ADR 0142 D8). One member per provider: the set is closed by product decision.</summary>
    Google = 3,
}
