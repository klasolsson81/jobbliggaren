using System.Globalization;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.Sessions;

/// <summary>
/// Startup validation for <see cref="SessionStoreOptions"/> (#746). The one constraint today is the
/// sliding-write throttle ceiling: <see cref="SessionStoreOptions.SlideThreshold"/> must be in
/// <c>[0.0, <see cref="MaxSlideThreshold"/>]</c>.
///
/// <para>
/// The throttle widens the orphan-index self-heal cadence to <c>SlideThreshold × SlidingTtl</c>
/// (worst case Persistent's 30d window). That self-heal is a security-relevant property (it keeps
/// <c>InvalidateAllForUserAsync</c> able to reach a session whose index membership was lost to a
/// partial non-atomic write — TD-23), so it must not be pushed past ~a week even by a configuration
/// typo. The ceiling is therefore enforced mechanically at startup (invalid states unrepresentable)
/// rather than by discipline (CTO bind 2026-07-19 — a production ratchet above 0.1 additionally
/// needs security-auditor sign-off). Registered with <c>ValidateOnStart()</c> so a bad value fails
/// the boot, not a later request.
/// </para>
/// </summary>
public sealed class SessionStoreOptionsValidator : IValidateOptions<SessionStoreOptions>
{
    public const double MaxSlideThreshold = 0.25;

    public ValidateOptionsResult Validate(string? name, SessionStoreOptions options)
    {
        // double.IsFinite first: NaN is neither < 0.0 nor > Max, so a "NaN" config value would slip
        // through the range check and then throw ArgumentException at TimeSpan * NaN on EVERY read
        // (RedisSessionStore.GetAsync) — moving the failure from boot to the hot path, exactly what
        // ValidateOnStart is meant to prevent. (±Infinity is already caught by the range check.)
        if (!double.IsFinite(options.SlideThreshold)
            || options.SlideThreshold is < 0.0 or > MaxSlideThreshold)
            return ValidateOptionsResult.Fail(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Session:SlideThreshold must be in [0.0, {MaxSlideThreshold}] (was {options.SlideThreshold})."));

        return ValidateOptionsResult.Success;
    }
}
