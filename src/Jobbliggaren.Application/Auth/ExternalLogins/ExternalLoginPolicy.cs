namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// The external-login flow's bounds (ADR 0142 "Attempt budget": OAuth state ≤ 10 min, cookie mandatory, record
/// taken once). Constants, like <c>LoginChallengePolicy</c>'s, so no environment can move them without a PR.
/// </summary>
public static class ExternalLoginPolicy
{
    /// <summary>How long a started flow lives. The web's state cookie mirrors this number.</summary>
    public static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The longest post-login path a flow carries. The web's flow cookie holds no longer one, and the volatile
    /// Redis, which refuses writes when full, is never handed an unbounded value.
    /// </summary>
    public const int MaxNextLength = 512;

    /// <summary>The longest authorization code accepted. A provider's code is far shorter; this bounds the input.</summary>
    public const int MaxCodeLength = 512;
}
