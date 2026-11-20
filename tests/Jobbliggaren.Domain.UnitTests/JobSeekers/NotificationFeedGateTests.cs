using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;
using static Jobbliggaren.Domain.UnitTests.JobSeekers.ConsentLifecycleDriver;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// The three notification-bell gates (#2130, ADR 0162 D6; #1663 PR 3, ADR 0175 D8) over every state the aggregate's
/// own methods produce: both consent lifecycles × the all-mail pause × the three mail-type pauses × the three bell
/// switches. Matches reach the bell exactly when the background-match consent is granted AND the matches switch is on
/// (setting AND consent, never OR; security V1). Followed ads and Branschbevakning ads each reach it exactly when their
/// own switch is on, with no consent. No gate reads a mail pause: the bell ignores them (ADR 0164 D1). The query form and
/// the instance form agree.
/// </summary>
public class NotificationFeedGateTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    public static TheoryData<Lifecycle, Lifecycle, bool, bool, bool, bool, bool> EveryProducibleState()
    {
        var states = new TheoryData<Lifecycle, Lifecycle, bool, bool, bool, bool, bool>();
        bool[] onOrOff = [true, false];
        foreach (var matchConsent in Enum.GetValues<Lifecycle>())
            foreach (var followConsent in Enum.GetValues<Lifecycle>())
                foreach (var allMailOn in onOrOff)
                    foreach (var otherMailOn in onOrOff)
                        foreach (var feedMatchesOn in onOrOff)
                            foreach (var feedFollowedOn in onOrOff)
                                foreach (var feedCriterionOn in onOrOff)
                                    states.Add(matchConsent, followConsent, allMailOn, otherMailOn, feedMatchesOn,
                                        feedFollowedOn, feedCriterionOn);
        return states;
    }

    [Theory]
    [MemberData(nameof(EveryProducibleState))]
    public void Matches_OverEveryProducibleState_AdmitsExactlyWhenTheConsentIsGrantedAndTheTypeIsOn(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool otherMailOn, bool feedMatchesOn,
        bool feedFollowedOn, bool feedCriterionOn)
    {
        var seeker = Produce(
            matchConsent, followConsent, allMailOn, otherMailOn, feedMatchesOn, feedFollowedOn, feedCriterionOn);

        ShouldAdmit(NotificationFeedGate.Matches, seeker, Granted(matchConsent) && feedMatchesOn);
    }

    [Theory]
    [MemberData(nameof(EveryProducibleState))]
    public void FollowedAds_OverEveryProducibleState_AdmitsExactlyWhenItsOwnTypeIsOn(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool otherMailOn, bool feedMatchesOn,
        bool feedFollowedOn, bool feedCriterionOn)
    {
        var seeker = Produce(
            matchConsent, followConsent, allMailOn, otherMailOn, feedMatchesOn, feedFollowedOn, feedCriterionOn);

        // No consent is read: the follow rows are produced for every active follow (Art. 6(1)(b)), and the follow
        // consent governs the e-mail only.
        ShouldAdmit(NotificationFeedGate.FollowedAds, seeker, feedFollowedOn);
    }

    [Theory]
    [MemberData(nameof(EveryProducibleState))]
    public void CriterionAds_OverEveryProducibleState_AdmitsExactlyWhenItsOwnTypeIsOn(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool otherMailOn, bool feedMatchesOn,
        bool feedFollowedOn, bool feedCriterionOn)
    {
        var seeker = Produce(
            matchConsent, followConsent, allMailOn, otherMailOn, feedMatchesOn, feedFollowedOn, feedCriterionOn);

        // No consent is read: Branschbevakning hits are produced under Art. 6(1)(b) for every criterion, and no consent
        // can be what gates reading them (DPIA 560 §D9, ruling (2)).
        ShouldAdmit(NotificationFeedGate.CriterionAds, seeker, feedCriterionOn);
    }

    private static void ShouldAdmit(NotificationFeedGate gate, JobSeeker seeker, bool expected)
    {
        gate.IsSatisfiedBy(seeker).ShouldBe(expected);

        var applied = gate.Apply(new[] { seeker }.AsQueryable()).ToList();
        applied.Contains(seeker).ShouldBe(expected);
    }

    private static JobSeeker Produce(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool otherMailOn, bool feedMatchesOn,
        bool feedFollowedOn, bool feedCriterionOn)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;
        Apply(matchConsent, seeker.UpdateNotificationConsent, fromHour: 1);
        Apply(followConsent, seeker.UpdateFollowedCompanyNotificationConsent, fromHour: 4);
        seeker.SetNotificationMail(allMailOn, Later(7));
        seeker.SetTopMatchMail(otherMailOn, Later(8));
        seeker.SetMatchSummaryMail(otherMailOn, Later(9));
        seeker.SetNotificationFeedMatches(feedMatchesOn, Later(10));
        seeker.SetNotificationFeedFollowedAds(feedFollowedOn, Later(11));
        seeker.SetNotificationFeedCriterionAds(feedCriterionOn, Later(12));
        return seeker;
    }

    private static bool Granted(Lifecycle lifecycle) =>
        lifecycle is Lifecycle.Granted or Lifecycle.GrantedAgain;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));
}
