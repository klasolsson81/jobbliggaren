using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class SessionStoreOptions
{
    public const string SectionName = "Session";

    // One profile per SessionLifetime. Defaults are the source of truth; appsettings
    // may override any field. The lifetime a session was created under is persisted,
    // so GetAsync selects the same profile on every read.

    // Legacy = the pre-profiles reach (14d sliding / 30d cap, PR1), so existing
    // sessions and any login before the "Håll mig inloggad" checkbox ships keep
    // exactly today's behaviour. Never rotates.
    public SessionLifetimeProfile Legacy { get; init; } = new()
    {
        SlidingTtl = TimeSpan.FromDays(14),
        AbsoluteTtl = TimeSpan.FromDays(30),
        RotationInterval = TimeSpan.Zero,
    };

    // Session = "Håll mig inloggad" unchecked. Short-lived so the reach is bounded
    // even in browsers that restore the session cookie on restart (security R1). No
    // rotation (the window is already short).
    public SessionLifetimeProfile Session { get; init; } = new()
    {
        SlidingTtl = TimeSpan.FromHours(24),
        AbsoluteTtl = TimeSpan.FromHours(24),
        RotationInterval = TimeSpan.Zero,
    };

    // Persistent = "Håll mig inloggad" checked. 30d sliding + session-id rotation every
    // 24h. The cap stays <= 30d until the rotation DRIVER (the middleware that calls
    // /auth/refresh) ships in the activation PR — security C3/COND-2: no > 30d reach
    // until rotation is actually driven, at which point the cap is bumped to 180d
    // atomically with the driver. Rotation is defined here but dormant (nothing threads
    // rememberMe -> Persistent yet).
    public SessionLifetimeProfile Persistent { get; init; } = new()
    {
        SlidingTtl = TimeSpan.FromDays(30),
        AbsoluteTtl = TimeSpan.FromDays(30),
        RotationInterval = TimeSpan.FromHours(24),
    };

    // How long a rotation "claim" key lives (SET NX) — just long enough to serialize a
    // concurrent burst of refresh requests so exactly one rotates. After a rotation the
    // interval-gate (RotatedAt) blocks re-rotation for RotationInterval, so this only
    // guards the momentary race, not the steady state.
    public TimeSpan RotationClaimTtl { get; init; } = TimeSpan.FromSeconds(30);

    public SessionLifetimeProfile ProfileFor(SessionLifetime lifetime) => lifetime switch
    {
        SessionLifetime.Persistent => Persistent,
        SessionLifetime.Session => Session,
        _ => Legacy,
    };
}

public sealed class SessionLifetimeProfile
{
    // Sliding window: a read renews the key to now + SlidingTtl (clamped to the cap).
    public TimeSpan SlidingTtl { get; init; } = TimeSpan.FromDays(14);

    // Absolute ceiling from CreatedAt, enforced however actively the session is used.
    // Defaults finite (never zero — a zero cap would evict every session on first read).
    public TimeSpan AbsoluteTtl { get; init; } = TimeSpan.FromDays(30);

    // Rotate the session id once this has elapsed since the last rotation.
    // Zero disables rotation for the profile.
    public TimeSpan RotationInterval { get; init; } = TimeSpan.Zero;
}
