using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// The budgets of a change-email request (ADR 0142 D5). A scope's name
/// becomes part of a Redis key and must never change once shipped.
/// </summary>
public static class ChangeEmailPolicy
{
    /// <summary>
    /// New addresses one USER may ask to move to per 24 hours. Every request mails an address nobody has proven,
    /// and a made-up one bounces; the per-request cooldown alone would still admit one such mail a minute.
    /// </summary>
    public static readonly RateBudgetScope UserTargetsDailyBudget =
        new("change-email-targets-daily", limit: 5, window: TimeSpan.FromHours(24));

    /// <summary>
    /// Codes to one new ADDRESS per 24 hours, whoever asks. It is what bounds guessing per address: the per-user
    /// budgets bound one guesser, and a code guessed right would attach an address its owner never proved
    /// (security-auditor, PR 4's panel).
    /// </summary>
    public static readonly RateBudgetScope PerTargetDailyBudget =
        new("change-email-per-target-daily", limit: 3, window: TimeSpan.FromHours(24));

    /// <summary>Per USER: one change-email request per window (<c>AuthEmailCooldownOptions.ChangeEmailWindowSeconds</c>).</summary>
    public static RateBudgetScope UserCooldown(TimeSpan window) =>
        new("change-email-user", limit: 1, window: window);

    /// <summary>Per TARGET address: one change-email mail per window, whoever asks.</summary>
    public static RateBudgetScope TargetCooldown(TimeSpan window) =>
        new("change-email-target", limit: 1, window: window);
}
