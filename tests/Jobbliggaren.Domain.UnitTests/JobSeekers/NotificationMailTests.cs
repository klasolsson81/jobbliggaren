using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;
using static Jobbliggaren.Domain.UnitTests.JobSeekers.ConsentLifecycleDriver;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// The three mail gates (ADR 0164) over every state the aggregate's own methods produce: both consent
/// lifecycles × the all-mail switch × both mail-type switches. A gate admits a seeker exactly when its own
/// consent is granted, all mail is on and, for Toppmatch and the summary, its own type is on; it reads no
/// other dimension. The query form and the instance form agree in LINQ to objects.
/// </summary>
public class NotificationMailTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    public static TheoryData<Lifecycle, Lifecycle, bool, bool, bool> EveryProducibleState()
    {
        var states = new TheoryData<Lifecycle, Lifecycle, bool, bool, bool>();
        bool[] onOrOff = [true, false];
        foreach (var matchConsent in Enum.GetValues<Lifecycle>())
            foreach (var followConsent in Enum.GetValues<Lifecycle>())
                foreach (var allMailOn in onOrOff)
                    foreach (var topMatchMailOn in onOrOff)
                        foreach (var matchSummaryMailOn in onOrOff)
                            states.Add(matchConsent, followConsent, allMailOn, topMatchMailOn, matchSummaryMailOn);
        return states;
    }

    [Theory]
    [MemberData(nameof(EveryProducibleState))]
    public void TopMatch_OverEveryProducibleState_AdmitsExactlyWhenMatchConsentAllMailAndTopMatchMailAreOn(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool topMatchMailOn, bool matchSummaryMailOn)
    {
        var seeker = Produce(matchConsent, followConsent, allMailOn, topMatchMailOn, matchSummaryMailOn);

        ShouldAdmit(NotificationMail.TopMatch, seeker, Granted(matchConsent) && allMailOn && topMatchMailOn);
    }

    [Theory]
    [MemberData(nameof(EveryProducibleState))]
    public void MatchSummary_OverEveryProducibleState_AdmitsExactlyWhenMatchConsentAllMailAndSummaryMailAreOn(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool topMatchMailOn, bool matchSummaryMailOn)
    {
        var seeker = Produce(matchConsent, followConsent, allMailOn, topMatchMailOn, matchSummaryMailOn);

        ShouldAdmit(NotificationMail.MatchSummary, seeker, Granted(matchConsent) && allMailOn && matchSummaryMailOn);
    }

    [Theory]
    [MemberData(nameof(EveryProducibleState))]
    public void FollowedCompany_OverEveryProducibleState_AdmitsExactlyWhenFollowConsentAndAllMailAreOn(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool topMatchMailOn, bool matchSummaryMailOn)
    {
        var seeker = Produce(matchConsent, followConsent, allMailOn, topMatchMailOn, matchSummaryMailOn);

        ShouldAdmit(NotificationMail.FollowedCompany, seeker, Granted(followConsent) && allMailOn);
    }

    private static void ShouldAdmit(NotificationMailGate gate, JobSeeker seeker, bool expected)
    {
        gate.IsSatisfiedBy(seeker).ShouldBe(expected);

        var applied = gate.Apply(new[] { seeker }.AsQueryable()).ToList();
        applied.Contains(seeker).ShouldBe(expected);
    }

    private static JobSeeker Produce(
        Lifecycle matchConsent, Lifecycle followConsent, bool allMailOn, bool topMatchMailOn, bool matchSummaryMailOn)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;
        Apply(matchConsent, seeker.UpdateNotificationConsent, fromHour: 1);
        Apply(followConsent, seeker.UpdateFollowedCompanyNotificationConsent, fromHour: 4);
        seeker.SetNotificationMail(allMailOn, Later(7));
        seeker.SetTopMatchMail(topMatchMailOn, Later(8));
        seeker.SetMatchSummaryMail(matchSummaryMailOn, Later(9));
        return seeker;
    }

    private static bool Granted(Lifecycle lifecycle) =>
        lifecycle is Lifecycle.Granted or Lifecycle.GrantedAgain;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));
}
