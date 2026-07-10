using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.Security.BreachCheck;

/// <summary>
/// HIBP Pwned Passwords breach-check configuration (#616). Bound from the "BreachCheck" section
/// with ValidateDataAnnotations + ValidateOnStart. Every default is valid, so no environment
/// (CI, Testcontainers, dev) needs a config section for the app to start — the section exists to
/// tune the CTO-bound resilience budget and to reach the kill switch, not to enable the feature.
/// </summary>
public sealed class BreachCheckOptions
{
    public const string SectionName = "BreachCheck";

    /// <summary>
    /// Pwned Passwords base URL. The trailing slash is REQUIRED for the relative
    /// <c>range/{prefix}</c> request URI to resolve under the base path.
    /// </summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.pwnedpasswords.com/";

    /// <summary>
    /// Kill switch (offline dev boxes, HIBP emergency). Default TRUE — the check is opt-out.
    /// When false, <see cref="DisabledBreachedPasswordChecker"/> is registered instead of the
    /// HTTP client, so the Identity validator chain stays identical in both modes.
    ///
    /// <para>
    /// Read at COMPOSITION time via raw <c>configuration.GetValue</c> in
    /// <c>AddBreachedPasswordCheck</c> (the flag decides WHICH implementation is registered,
    /// before the provider exists). This bound copy mirrors the value for
    /// diagnostics/completeness and is intentionally not a runtime toggle.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Total attempt budget in seconds before the check fails open (CTO-bind: ~2 s, retry 0 —
    /// register/change-password are interactive hot paths, not batch ingest).
    /// </summary>
    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 2;

    /// <summary>Minimum calls in the sampling window before the circuit can open.</summary>
    [Range(2, 100)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>Failure ratio that opens the circuit (high — only a hard outage should trip it).</summary>
    [Range(0.05, 1.0)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.9;

    /// <summary>Sampling window in seconds for the failure ratio.</summary>
    [Range(5, 300)]
    public int CircuitBreakerSamplingSeconds { get; set; } = 30;

    /// <summary>
    /// How long the circuit stays open (CTO-bind: 30–60 s). While open, every check returns
    /// <c>Unavailable</c> immediately — fail-open with zero added latency during an outage.
    /// </summary>
    [Range(1, 600)]
    public int CircuitBreakerBreakSeconds { get; set; } = 45;
}
