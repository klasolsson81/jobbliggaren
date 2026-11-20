using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// ADR 0188 (#2265) — the junior quick link's switch on <see cref="JobSeeker"/>. A new seeker shows the link; off is
/// stamped once and kept while it stays off; on clears it; the same state is a no-op. It writes its own stamp and no
/// other preference.
/// </summary>
public class JobSeekerJuniorQuickLinkTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    [Fact]
    public void Register_NewSeeker_ShowsTheJuniorLink()
    {
        NewSeeker().Preferences.JuniorQuickLinkOffAt.ShouldBeNull();
    }

    [Fact]
    public void SetJuniorQuickLink_OffFromOn_StampsTheClocksNowAndBumpsUpdatedAt()
    {
        var seeker = NewSeeker();
        var offClock = Later(2);

        seeker.SetJuniorQuickLink(on: false, offClock);

        seeker.Preferences.JuniorQuickLinkOffAt.ShouldBe(offClock.UtcNow);
        seeker.UpdatedAt.ShouldBe(offClock.UtcNow);
    }

    [Fact]
    public void SetJuniorQuickLink_OffWhileOff_KeepsTheFirstStampAndDoesNotBumpUpdatedAt()
    {
        var seeker = NewSeeker();
        var firstOff = Later(2);
        seeker.SetJuniorQuickLink(on: false, firstOff);
        var preferencesBefore = seeker.Preferences;

        seeker.SetJuniorQuickLink(on: false, Later(7));

        seeker.Preferences.JuniorQuickLinkOffAt.ShouldBe(firstOff.UtcNow);
        seeker.Preferences.ShouldBeSameAs(preferencesBefore);
        seeker.UpdatedAt.ShouldBe(firstOff.UtcNow);
    }

    [Fact]
    public void SetJuniorQuickLink_OnFromOff_ClearsTheStampAndBumpsUpdatedAt()
    {
        var seeker = NewSeeker();
        seeker.SetJuniorQuickLink(on: false, Later(2));
        var onClock = Later(5);

        seeker.SetJuniorQuickLink(on: true, onClock);

        seeker.Preferences.JuniorQuickLinkOffAt.ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(onClock.UtcNow);
    }

    [Fact]
    public void SetJuniorQuickLink_OnWhileOn_IsNoOpAndDoesNotBumpUpdatedAt()
    {
        var seeker = NewSeeker();
        var preferencesBefore = seeker.Preferences;
        var updatedBefore = seeker.UpdatedAt;

        seeker.SetJuniorQuickLink(on: true, Later(7));

        seeker.Preferences.ShouldBeSameAs(preferencesBefore);
        seeker.UpdatedAt.ShouldBe(updatedBefore);
    }

    [Fact]
    public void SetJuniorQuickLink_WritesNoOtherPreference()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, Later(1));
        seeker.SetNotificationFeedMatches(on: false, Later(2));
        var before = seeker.Preferences;

        seeker.SetJuniorQuickLink(on: false, Later(3));

        (seeker.Preferences with { JuniorQuickLinkOffAt = null }).ShouldBe(before);
    }
}
