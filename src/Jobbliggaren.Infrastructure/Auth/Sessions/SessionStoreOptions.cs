namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class SessionStoreOptions
{
    public const string SectionName = "Session";

    // Sliding window: a session's key is renewed to now + SlidingTtl on every read,
    // so an actively-used session never expires from inactivity within this window.
    public TimeSpan SlidingTtl { get; init; } = TimeSpan.FromDays(14);

    // Absolute lifetime cap: a hard ceiling from CreatedAt, enforced regardless of
    // activity. Closes #481 Low ("a session slides forever while used"). Kept <= 30d
    // for the current single-lifetime sessions (no session-id rotation yet — rotation
    // and the longer persistent-branch cap arrive with the "Håll mig inloggad" branch).
    public TimeSpan AbsoluteTtl { get; init; } = TimeSpan.FromDays(30);
}
