using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// The external-login flow's bounds (ADR 0142 "Attempt budget": OAuth state ≤ 10 min, cookie mandatory, record
/// taken once). Constants, like <c>LoginChallengePolicy</c>'s, so no environment can move them without a PR.
/// </summary>
public static class ExternalLoginPolicy
{
    /// <summary>How long a started flow lives.</summary>
    public static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    /// <summary>The longest post-login path a flow carries.</summary>
    public const int MaxNextLength = 512;

    /// <summary>The longest authorization code accepted. A provider's code is far shorter; this bounds the input.</summary>
    public const int MaxCodeLength = 512;

    /// <summary>
    /// Started flows, every provider together, per window (security-auditor PR S m3). A start is a GET any page can
    /// reach, and each one writes a record to the volatile store, which refuses every write, a login code's included,
    /// once it is full. Past it a start is refused before anything is written, so a flood stops external login and
    /// never code login.
    /// </summary>
    public static readonly RateBudgetScope StartBudget =
        new("external-login-starts", limit: 60, window: TimeSpan.FromMinutes(1));

    /// <summary>The one subject <see cref="StartBudget"/> counts against.</summary>
    public const string StartBudgetSubject = "every-external-login-start";
}
