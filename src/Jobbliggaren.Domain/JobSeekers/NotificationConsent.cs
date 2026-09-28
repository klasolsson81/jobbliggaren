using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// The two notification consents, one definition each (ADR 0080, ADR 0087 D5): the flag is on and no
/// withdrawal is recorded. Every due set, every check before a claim and the scan's per-attempt check
/// decide consent through these, so no two of them can drift apart (GDPR Art. 5(1)(d)).
/// <para>
/// A consent says nothing about whether the account still exists. Read the row through the
/// <see cref="JobSeeker"/> query filter, which excludes a soft-deleted account.
/// </para>
/// </summary>
public static class NotificationConsent
{
    public static Specification<JobSeeker> BackgroundMatch { get; } = new(seeker =>
        seeker.Preferences.BackgroundMatchNotificationsEnabled
        && seeker.Preferences.NotificationConsentWithdrawnAt == null);

    public static Specification<JobSeeker> FollowedCompany { get; } = new(seeker =>
        seeker.Preferences.FollowedCompanyNotificationsEnabled
        && seeker.Preferences.FollowedCompanyNotificationConsentWithdrawnAt == null);
}
