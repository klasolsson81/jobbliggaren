namespace Jobbliggaren.Application.Auth;

/// <summary>How a session was earned: the login-succeeded audit event records it (#1735).</summary>
public enum LoginMethod
{
    Password,
    Code,
    Link,
}
