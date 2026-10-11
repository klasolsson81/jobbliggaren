using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;
using Lifecycle = Jobbliggaren.Domain.UnitTests.JobSeekers.NotificationConsentTests.Lifecycle;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// ADR 0164 (#2107) — the three mail switches on <see cref="JobSeeker"/>. Off is a pause stamped once and
/// kept while it stays off, on clears it, and the same state is a no-op. Each switch writes its own stamp
/// and nothing else: never a consent, a withdrawal, the cadence or another switch (security V3).
/// </summary>
public class JobSeekerNotificationMailTests
{
    public enum Setter
    {
        SetNotificationMail,
        SetTopMatchMail,
        SetMatchSummaryMail,
    }

    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    [Fact]
    public void Register_NewSeeker_HasEveryMailSwitchOn()
    {
        var prefs = NewSeeker().Preferences;

        prefs.NotificationMailOffAt.ShouldBeNull();
        prefs.TopMatchMailOffAt.ShouldBeNull();
        prefs.MatchSummaryMailOffAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(Setter.SetNotificationMail)]
    [InlineData(Setter.SetTopMatchMail)]
    [InlineData(Setter.SetMatchSummaryMail)]
    public void EachMailSetter_OffFromOn_StampsTheClocksNowAndBumpsUpdatedAt(Setter setter)
    {
        var seeker = NewSeeker();
        var offClock = Later(2);

        Set(seeker, setter, on: false, offClock);

        Stamp(seeker.Preferences, setter).ShouldBe(offClock.UtcNow);
        seeker.UpdatedAt.ShouldBe(offClock.UtcNow);
    }

    [Theory]
    [InlineData(Setter.SetNotificationMail)]
    [InlineData(Setter.SetTopMatchMail)]
    [InlineData(Setter.SetMatchSummaryMail)]
    public void EachMailSetter_OffWhileOff_KeepsTheFirstStampAndDoesNotBumpUpdatedAt(Setter setter)
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
    [InlineData(Setter.SetNotificationMail)]
    [InlineData(Setter.SetTopMatchMail)]
    [InlineData(Setter.SetMatchSummaryMail)]
    public void EachMailSetter_OnFromOff_ClearsTheStampAndBumpsUpdatedAt(Setter setter)
    {
        var seeker = NewSeeker();
        Set(seeker, setter, on: false, Later(2));
        var onClock = Later(5);

        Set(seeker, setter, on: true, onClock);

        Stamp(seeker.Preferences, setter).ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(onClock.UtcNow);
    }

    [Theory]
    [InlineData(Setter.SetNotificationMail)]
    [InlineData(Setter.SetTopMatchMail)]
    [InlineData(Setter.SetMatchSummaryMail)]
    public void EachMailSetter_OnWhileOn_IsNoOpAndDoesNotBumpUpdatedAt(Setter setter)
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
    [InlineData(Setter.SetNotificationMail)]
    [InlineData(Setter.SetTopMatchMail)]
    [InlineData(Setter.SetMatchSummaryMail)]
    public void EachMailSetter_OffAgainAfterSwitchingBackOn_TakesANewStamp(Setter setter)
    {
        var seeker = NewSeeker();
        Set(seeker, setter, on: false, Later(2));
        Set(seeker, setter, on: true, Later(3));
        var secondOff = Later(7);

        Set(seeker, setter, on: false, secondOff);

        Stamp(seeker.Preferences, setter).ShouldBe(secondOff.UtcNow);
    }

    [Fact]
    public void SetNotificationMail_On_GrantsNeitherConsent()
    {
        var seeker = NewSeeker();
        seeker.SetNotificationMail(on: false, Later(1));

        seeker.SetNotificationMail(on: true, Later(2));

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBeNull();
    }

    [Fact]
    public void SetNotificationMail_OnAfterAPause_LeavesASwitchedOffMailTypeOff()
    {
        var seeker = NewSeeker();
        var topMatchOff = Later(1);
        var summaryOff = Later(2);
        seeker.SetTopMatchMail(on: false, topMatchOff);
        seeker.SetMatchSummaryMail(on: false, summaryOff);
        seeker.SetNotificationMail(on: false, Later(3));

        seeker.SetNotificationMail(on: true, Later(4));

        seeker.Preferences.TopMatchMailOffAt.ShouldBe(topMatchOff.UtcNow);
        seeker.Preferences.MatchSummaryMailOffAt.ShouldBe(summaryOff.UtcNow);
    }

    [Fact]
    public void SetNotificationMail_Off_StampsNoWithdrawalOfEitherConsent()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, Later(1));
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(2));

        seeker.SetNotificationMail(on: false, Later(3));

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
                    foreach (var otherSwitchesOff in new[] { false, true })
                        cases.Add(setter, on, consents, otherSwitchesOff);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EverySetterDirectionAndBeforeState))]
    public void EachMailSetter_InEitherDirection_WritesItsOwnStampAndNothingElse(
        Setter setter, bool on, Lifecycle consents, bool otherSwitchesOff)
    {
        var seeker = NewSeeker();
        Apply(consents, seeker.UpdateNotificationConsent, fromHour: 1);
        Apply(consents, seeker.UpdateFollowedCompanyNotificationConsent, fromHour: 4);
        seeker.SetDigestCadence(DigestCadence.Daily, Later(7)).IsSuccess.ShouldBeTrue();
        foreach (var other in Enum.GetValues<Setter>().Where(other => other != setter))
            Set(seeker, other, on: !otherSwitchesOff, Later(8 + (int)other));
        if (on)
            Set(seeker, setter, on: false, Later(8 + (int)setter));
        var before = seeker.Preferences;
        var actClock = Later(12);

        Set(seeker, setter, on, actClock);

        seeker.Preferences.ShouldBe(
            WithStamp(before, setter, on ? null : actClock.UtcNow),
            "a mail switch writes its own stamp and nothing else (security V3)");
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
    public void EitherConsentUpdate_FromEveryLifecycleState_KeepsEveryPausedMailSwitchPaused(
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

    private static void Set(JobSeeker seeker, Setter setter, bool on, IDateTimeProvider clock)
    {
        Action<bool, IDateTimeProvider> set = setter switch
        {
            Setter.SetNotificationMail => seeker.SetNotificationMail,
            Setter.SetTopMatchMail => seeker.SetTopMatchMail,
            Setter.SetMatchSummaryMail => seeker.SetMatchSummaryMail,
            _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
        };
        set(on, clock);
    }

    private static DateTimeOffset? Stamp(Preferences preferences, Setter setter) => setter switch
    {
        Setter.SetNotificationMail => preferences.NotificationMailOffAt,
        Setter.SetTopMatchMail => preferences.TopMatchMailOffAt,
        Setter.SetMatchSummaryMail => preferences.MatchSummaryMailOffAt,
        _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
    };

    private static Preferences WithStamp(Preferences preferences, Setter setter, DateTimeOffset? stamp) => setter switch
    {
        Setter.SetNotificationMail => preferences with { NotificationMailOffAt = stamp },
        Setter.SetTopMatchMail => preferences with { TopMatchMailOffAt = stamp },
        Setter.SetMatchSummaryMail => preferences with { MatchSummaryMailOffAt = stamp },
        _ => throw new ArgumentOutOfRangeException(nameof(setter), setter, null),
    };

    private static void Apply(Lifecycle lifecycle, Action<bool, IDateTimeProvider> setConsent, int fromHour)
    {
        if (lifecycle == Lifecycle.NeverGranted)
            return;

        setConsent(true, Later(fromHour));
        if (lifecycle == Lifecycle.Granted)
            return;

        setConsent(false, Later(fromHour + 1));
        if (lifecycle == Lifecycle.Withdrawn)
            return;

        setConsent(true, Later(fromHour + 2));
    }
}
