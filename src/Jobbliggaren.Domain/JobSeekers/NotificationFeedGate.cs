using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// Whether a notification type reaches the bell (ADR 0168, #2130): the user's own switch for the type and, for a
/// match, the consent its rows were produced under. Setting AND consent, never OR (security V1): a switch is a
/// delivery choice and grants nothing, and a consent says nothing about whether the user wants the bell to show
/// it. The consent is held by reference to <see cref="NotificationConsent"/>, never copied, so the bell and the
/// mail senders decide it through the one definition.
/// <para>
/// The bell reads <b>no mail pause</b> (ADR 0164 D1): the all-mail pause and the mail-type pauses stop e-mail and
/// nothing else, so no part of this gate may name them. That is why this is not a use of
/// <see cref="NotificationMailGate"/>, which conjoins the all-mail pause by construction.
/// </para>
/// <para>
/// A gate says nothing about whether the account still exists. Apply it to the <see cref="JobSeeker"/> set, whose
/// query filter excludes a soft-deleted account.
/// </para>
/// </summary>
public sealed class NotificationFeedGate
{
    private readonly Specification<JobSeeker>? _consent;
    private readonly Specification<JobSeeker> _typeOn;

    private NotificationFeedGate(Specification<JobSeeker>? consent, Specification<JobSeeker> typeOn)
    {
        _consent = consent;
        _typeOn = typeOn;
    }

    /// <summary>New background matches: the background-match consent and the matches switch on.</summary>
    public static NotificationFeedGate Matches { get; } = new(
        NotificationConsent.BackgroundMatch,
        new Specification<JobSeeker>(seeker => seeker.Preferences.NotificationFeedMatchesOffAt == null));

    /// <summary>
    /// New ads from followed companies: the followed-ads switch alone. Their rows are produced for every active
    /// follow without a consent, and the follow consent governs e-mail only (ADR 0162 D6).
    /// </summary>
    public static NotificationFeedGate FollowedAds { get; } = new(
        consent: null,
        new Specification<JobSeeker>(seeker => seeker.Preferences.NotificationFeedFollowedAdsOffAt == null));

    /// <summary>
    /// The query form: each part is its own <c>Where</c>, so nothing is rewritten and every part translates as it
    /// does on its own.
    /// </summary>
    public IQueryable<JobSeeker> Apply(IQueryable<JobSeeker> seekers)
    {
        var gated = _consent is null ? seekers : seekers.Where(_consent.Criteria);
        return gated.Where(_typeOn.Criteria);
    }

    public bool IsSatisfiedBy(JobSeeker seeker) =>
        (_consent?.IsSatisfiedBy(seeker) ?? true) && _typeOn.IsSatisfiedBy(seeker);
}
