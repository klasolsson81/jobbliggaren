using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;
using static Jobbliggaren.Domain.UnitTests.JobSeekers.ConsentLifecycleDriver;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// #2130 (ADR 0162, PR 2) — the two in-app switches on <see cref="JobSeeker"/>: whether new matches and new
/// ads from followed companies reach the bell. Off is stamped once and kept while it stays off, on clears
/// it, and the same state is a no-op. Each switch writes its own stamp and nothing else: never a consent, a
/// withdrawal, an e-mail pause, the cadence or the other switch (security V3), because a switch for what
/// reaches the bell is a delivery choice, never a consent.
/// </summary>
public class JobSeekerNotificationFeedTests
{
    public enum Setter
    {
        SetNotificationFeedMatches,
        SetNotificationFeedFollowedAds,
    }

    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    [Fact]
    public void Register_NewSeeker_HasBothBellTypesOn()
    {
        var prefs = NewSeeker().Preferences;

        prefs.NotificationFeedMatchesOffAt.ShouldBeNull();
        prefs.NotificationFeedFollowedAdsOffAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(Setter.SetNotificationFeedMatches)]
    [InlineData(Setter.SetNotificationFeedFollowedAds)]
    public void EachSetter_OffFromOn_StampsTheClocksNowAndBumpsUpdatedAt(Setter setter)
    {
        var seeker = NewSeeker();
        var offClock = Later(2);

        Set(seeker, setter, on: false, offClock);

        Stamp(seeker.Preferences, setter).ShouldBe(offClock.UtcNow);
        seeker.UpdatedAt.ShouldBe(offClock.UtcNow);
    }

    [Theory]
    [InlineData(Setter.SetNotificationFeedMatches)]
    [InlineData(Setter.SetNotificationFeedFollowedAds)]
    public void EachSetter_OffWhileOff_KeepsTheFirstStampAndDoesNotBumpUpdatedAt(Setter setter)
    {
        var seeker = NewSeeker();
        var firstOff = Later(2);
        Set(seeker, setter, on: false, firstOff);
        var preferencesBefore = seeker.Preferences;

        Set(seeker, setter, on: false, Later(7));

        Stamp(seeker.Preferences, setter).ShouldBe(firstOff.UtcNow);
        seeker.Preferences.ShouldBeSameAs(preferencesBefore);
        seeker.UpdatedAt.ShouldBe(firstOff.UtcNow);
    }

    [Theory]
    [InlineData(Setter.SetNotificationFeedMatches)]
    [InlineData(Setter.SetNotificationFeedFollowedAds)]
    public void EachSetter_OnFromOff_ClearsTheStampAndBumpsUpdatedAt(Setter setter)
    {
        var seeker = NewSeeker();
        Set(seeker, setter, on: false, Later(2));
        var onClock = Later(5);

        Set(seeker, setter, on: true, onClock);

        Stamp(seeker.Preferences, setter).ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(onClock.UtcNow);
    }

    [Theory]
    [InlineData(Setter.SetNotificationFeedMatches)]
    [InlineData(Setter.SetNotificationFeedFollowedAds)]
    public void EachSetter_OnWhileOn_IsNoOpAndDoesNotBumpUpdatedAt(Setter setter)
    {
        var seeker = NewSeeker();
        var preferencesBefore = seeker.Preferences;
        var updatedBefore = seeker.UpdatedAt;

        Set(seeker, setter, on: true, Later(7));

        Stamp(seeker.Preferences, setter).ShouldBeNull();
        seeker.Preferences.ShouldBeSameAs(preferencesBefore);
        seeker.UpdatedAt.ShouldBe(updatedBefore);
    }

    [Theory]
    [InlineData(Setter.SetNotificationFeedMatches)]
    [InlineData(Setter.SetNotificationFeedFollowedAds)]
    public void EachSetter_OffAgainAfterSwitchingBackOn_TakesANewStamp(Setter setter)
    {
        var seeker = NewSeeker();
        Set(seeker, setter, on: false, Later(2));
        Set(seeker, setter, on: true, Later(3));
        var secondOff = Later(7);

        Set(seeker, setter, on: false, secondOff);

        Stamp(seeker.Preferences, setter).ShouldBe(secondOff.UtcNow);
    }

    [Fact]
    public void SetNotificationFeedMatches_OnAfterOff_GrantsNoConsent()
    {
        var seeker = NewSeeker();
        seeker.SetNotificationFeedMatches(on: false, Later(1));

        seeker.SetNotificationFeedMatches(on: true, Later(2));

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBeNull();
    }

    [Fact]
    public void SetNotificationFeedMatches_Off_StampsNoWithdrawalOfEitherConsent()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, Later(1));
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(2));

        seeker.SetNotificationFeedMatches(on: false, Later(3));
        seeker.SetNotificationFeedFollowedAds(on: false, Later(4));

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    public static TheoryData<Setter, bool, Lifecycle, bool> EverySetterDirectionAndBeforeState()
    {
        var cases = new TheoryData<Setter, bool, Lifecycle, bool>();
        foreach (var setter in Enum.GetValues<Setter>())
            foreach (var on in new[] { false, true })
                foreach (var consents in Enum.GetValues<Lifecycle>())
                    foreach (var otherStateOff in new[] { false, true })
                        cases.Add(setter, on, consents, otherStateOff);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EverySetterDirectionAndBeforeState))]
    public void EachSetter_InEitherDirection_WritesItsOwnStampAndNothingElse(
        Setter setter, bool on, Lifecycle consents, bool otherStateOff)
    {
        var seeker = NewSeeker();
        Apply(consents, seeker.UpdateNotificationConsent, fromHour: 1);
        Apply(consents, seeker.UpdateFollowedCompanyNotificationConsent, fromHour: 4);
        seeker.SetDigestCadence(DigestCadence.Daily, Later(7)).IsSuccess.ShouldBeTrue();
        // Every other switch the user owns is in the "other" state: the e-mail pauses and the other bell type.
        seeker.SetNotificationMail(on: !otherStateOff, Later(8));
        seeker.SetTopMatchMail(on: !otherStateOff, Later(9));
        seeker.SetMatchSummaryMail(on: !otherStateOff, Later(10));
        foreach (var other in Enum.GetValues<Setter>().Where(other => other != setter))
            Set(seeker, other, on: !otherStateOff, Later(11 + (int)other));
        if (on)
            Set(seeker, setter, on: false, Later(11 + (int)setter));
        var before = seeker.Preferences;
        var actClock = Later(14);

        Set(seeker, setter, on, actClock);

        seeker.Preferences.ShouldBe(
            WithStamp(before, setter, on ? null : actClock.UtcNow),
            "a bell switch writes its own stamp and nothing else (security V3)");
    }

    [Theory]
    [InlineData(false, Lifecycle.NeverGranted)]
    [InlineData(false, Lifecycle.Granted)]
    [InlineData(false, Lifecycle.Withdrawn)]
    [InlineData(false, Lifecycle.GrantedAgain)]
    [InlineData(true, Lifecycle.NeverGranted)]
    [InlineData(true, Lifecycle.Granted)]
    [InlineData(true, Lifecycle.Withdrawn)]
    [InlineData(true, Lifecycle.GrantedAgain)]
    public void EitherConsentUpdate_FromEveryLifecycleState_KeepsEveryOffBellSwitchOff(
        bool followConsent, Lifecycle before)
    {
        var seeker = NewSeeker();
        Action<bool, IDateTimeProvider> update = followConsent
            ? seeker.UpdateFollowedCompanyNotificationConsent
            : seeker.UpdateNotificationConsent;
        Apply(before, update, fromHour: 1);
        foreach (var setter in Enum.GetValues<Setter>())
            Set(seeker, setter, on: false, Later(8 + (int)setter));
        var stampsBefore = Enum.GetValues<Setter>().Select(setter => Stamp(seeker.Preferences, setter)).ToList();

        update(before is not (Lifecycle.Granted or Lifecycle.GrantedAgain), Later(12));

        Enum.GetValues<Setter>().Select(setter => Stamp(seeker.Preferences, setter)).ShouldBe(stampsBefore);
    }

    [Fact]
    public void EveryMailPauseSetter_LeavesBothBellStampsAlone()
    {
        var seeker = NewSeeker();
        seeker.SetNotificationFeedMatches(on: false, Later(1));
        seeker.SetNotificationFeedFollowedAds(on: false, Later(2));
        var matches = seeker.Preferences.NotificationFeedMatchesOffAt;
        var followed = seeker.Preferences.NotificationFeedFollowedAdsOffAt;

        seeker.SetNotificationMail(on: false, Later(3));
        seeker.SetTopMatchMail(on: false, Later(4));
        seeker.SetMatchSummaryMail(on: false, Later(5));
        seeker.SetNotificationMail(on: true, Later(6));

        seeker.Preferences.NotificationFeedMatchesOffAt.ShouldBe(matches);
        seeker.Preferences.NotificationFeedFollowedAdsOffAt.ShouldBe(followed);
    }

    private static void Set(JobSeeker seeker, Setter setter, bool on, IDateTimeProvider clock)
    {
        Action<bool, IDateTimeProvider> set = setter switch
        {
            Setter.SetNotificationFeedMatches => seeker.SetNotificationFeedMatches,
            Setter.SetNotificationFeedFollowedAds => seeker.SetNotificationFeedFollowedAds,
            _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
        };
        set(on, clock);
    }

    private static DateTimeOffset? Stamp(Preferences preferences, Setter setter) => setter switch
    {
        Setter.SetNotificationFeedMatches => preferences.NotificationFeedMatchesOffAt,
        Setter.SetNotificationFeedFollowedAds => preferences.NotificationFeedFollowedAdsOffAt,
        _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
    };

    private static Preferences WithStamp(Preferences preferences, Setter setter, DateTimeOffset? stamp) => setter switch
    {
        Setter.SetNotificationFeedMatches => preferences with { NotificationFeedMatchesOffAt = stamp },
        Setter.SetNotificationFeedFollowedAds => preferences with { NotificationFeedFollowedAdsOffAt = stamp },
        _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
    };
}
