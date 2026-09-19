using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The parameters of the login challenge's attempt budget (ADR 0142 "Attempt budget"). They are constants
/// rather than configuration on purpose: the budget is a measured acceptance with lapse triggers, and
/// trigger 5 fires on ANY change to the code length, the attempt count or the mint budget. A value an
/// environment variable could move would let the acceptance lapse with no PR, no panel and no measurement
/// (senior-cto-advisor and security-auditor, 2026-09-19). Every consumer reads these; none types them twice.
/// </summary>
public static class LoginChallengePolicy
{
    /// <summary>Digits in a login code.</summary>
    public const int CodeLength = 6;

    /// <summary>Wrong codes a challenge absorbs before its code arm is burned.</summary>
    public const int MaxAttempts = 3;

    /// <summary>How long a challenge (code and link) lives. One expiry state for both arms.</summary>
    public static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(15);

    /// <summary>Mails of any kind per address per 10 minutes. A refusal sends nothing and writes nothing.</summary>
    public static readonly RateBudgetScope MailBudget =
        new("login-challenge-mails", limit: 3, window: TimeSpan.FromMinutes(10));

    /// <summary>
    /// Codes per address per 24 hours. Past it an existing account still gets a mail, carrying the link
    /// only (Klas, 2026-09-19, option (A)), so a third party who spends the budget cannot lock the owner out.
    /// </summary>
    public static readonly RateBudgetScope CodeBudget =
        new("login-challenge-codes", limit: 10, window: TimeSpan.FromHours(24));

    /// <summary>
    /// The silent per-address cooldown: one mint per window. The window is configuration
    /// (<c>AuthEmailCooldownOptions.LoginChallengeWindowSeconds</c>) because it does not enter the guess
    /// arithmetic — the two budgets above cap mints whatever it is.
    /// </summary>
    public static RateBudgetScope Cooldown(TimeSpan window) =>
        new("login-challenge-cooldown", limit: 1, window: window);
}
