using Jobbliggaren.Application.Auth.LoginChallenges;

namespace Jobbliggaren.Application.Auth.AccountEmailChanges;

/// <summary>
/// The lifetime of an address change an administrator starts (#1975, ADR 0153). Constants rather than configuration,
/// for the reason <see cref="LoginChallengePolicy"/> gives: the code's life is one of lapse trigger 5's quantities, and
/// every mail and the privacy policy state these values. The code's length and its attempts are the login challenge's
/// own, so trigger 5 keeps one home.
/// </summary>
public static class AccountEmailChangePolicy
{
    /// <summary>
    /// D: no change completes before it was started plus this. The account's current address is told at the start, so
    /// an objection has this long to stop it (Klas, 2026-10-04).
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromHours(72);

    /// <summary>W: how long the code can be used once <see cref="Delay"/> has run.</summary>
    public static readonly TimeSpan UsableWindow = TimeSpan.FromHours(24);

    /// <summary>The record's whole life, D + W. Whole seconds: the volatile ACL grants EXPIRE and never PEXPIRE.</summary>
    public static TimeSpan Ttl => Delay + UsableWindow;
}
