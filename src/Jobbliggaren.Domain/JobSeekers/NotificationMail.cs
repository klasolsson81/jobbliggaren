using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// Whether a notification mail may be sent to a job seeker (ADR 0164): its consent, the pause of every
/// notification mail, and its own mail type's pause. The consent is held by reference to
/// <see cref="NotificationConsent"/>, never copied, and the all-mail pause is part of every gate, so no
/// sender can check one half of the condition.
/// <para>
/// A consent says nothing about whether the account still exists. Apply the gate to the
/// <see cref="JobSeeker"/> set, whose query filter excludes a soft-deleted account.
/// </para>
/// </summary>
public sealed class NotificationMailGate
{
    private static readonly Specification<JobSeeker> AllMailOn =
        new(seeker => seeker.Preferences.NotificationMailOffAt == null);

    private readonly Specification<JobSeeker> _consent;
    private readonly Specification<JobSeeker>? _mailTypeOn;

    internal NotificationMailGate(Specification<JobSeeker> consent, Specification<JobSeeker>? mailTypeOn)
    {
        _consent = consent;
        _mailTypeOn = mailTypeOn;
    }

    /// <summary>
    /// The query form: each part is its own <c>Where</c>, so nothing is rewritten and every part translates
    /// as it does on its own.
    /// </summary>
    public IQueryable<JobSeeker> Apply(IQueryable<JobSeeker> seekers)
    {
        var gated = seekers.Where(_consent.Criteria).Where(AllMailOn.Criteria);
        return _mailTypeOn is null ? gated : gated.Where(_mailTypeOn.Criteria);
    }

    public bool IsSatisfiedBy(JobSeeker seeker) =>
        _consent.IsSatisfiedBy(seeker)
        && AllMailOn.IsSatisfiedBy(seeker)
        && (_mailTypeOn?.IsSatisfiedBy(seeker) ?? true);
}

/// <summary>The gate of each notification mail (ADR 0164).</summary>
public static class NotificationMail
{
    /// <summary>The Toppmatch mail: the background-match consent and the Toppmatch mail switched on.</summary>
    public static NotificationMailGate TopMatch { get; } = new(
        NotificationConsent.BackgroundMatch,
        new Specification<JobSeeker>(seeker => seeker.Preferences.TopMatchMailOffAt == null));

    /// <summary>The summary of Strong matches: the background-match consent and the summary switched on.</summary>
    public static NotificationMailGate MatchSummary { get; } = new(
        NotificationConsent.BackgroundMatch,
        new Specification<JobSeeker>(seeker => seeker.Preferences.MatchSummaryMailOffAt == null));

    /// <summary>The mail about companies the user follows: its own consent is that mail's only switch.</summary>
    public static NotificationMailGate FollowedCompany { get; } = new(NotificationConsent.FollowedCompany, null);
}
