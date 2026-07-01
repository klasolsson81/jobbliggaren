using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — the SEPARATE company-follow consent invariants on
/// <see cref="JobSeeker"/> (its OWN Art. 7(1) evidence stamped ONCE/immutable; Art. 7(3) withdrawal;
/// no spurious revocation) and the monotonic company-follow scan watermark
/// (<see cref="JobSeeker.LastCompanyWatchScanAt"/>, advanced by <c>AdvanceCompanyWatchScan</c>).
/// The two consents are INDEPENDENT (a company-follow toggle never touches the background-match
/// flag/timestamps and vice versa) and the cadence is SHARED (ADR 0087 D2 — the company-follow
/// consent method deliberately does not touch DigestCadence).
/// </summary>
public class JobSeekerFollowedCompanyConsentTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(ValidUserId, "Klas Olsson", BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    // ---------------------------------------------------------------
    // UpdateFollowedCompanyNotificationConsent — Art. 7 consent lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public void Register_DefaultsFollowedCompanyConsentOff()
    {
        var prefs = NewSeeker().Preferences;

        prefs.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBeNull();
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public void UpdateFollowedCompanyConsent_FirstEnable_StampsConsentAtAndSetsFlag()
    {
        var seeker = NewSeeker();
        var enableClock = Later(1);

        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, enableClock);

        var prefs = seeker.Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(enableClock.UtcNow);
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(enableClock.UtcNow);
    }

    [Fact]
    public void UpdateFollowedCompanyConsent_SecondEnable_DoesNotRestampConsentAt()
    {
        // Art. 7(1) evidence is immutable: the first-ever opt-in time is the consent record.
        var seeker = NewSeeker();
        var firstEnable = Later(1);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, firstEnable);

        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(3));

        seeker.Preferences.FollowedCompanyNotificationConsentAt.ShouldBe(firstEnable.UtcNow);
        seeker.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
        seeker.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void UpdateFollowedCompanyConsent_DisableFromEnabled_StampsWithdrawnAtAndKeepsConsentAt()
    {
        // Art. 7(3): the opt-out records a revocation time; the original consent evidence is kept.
        var seeker = NewSeeker();
        var enableClock = Later(1);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, enableClock);

        var disableClock = Later(4);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, disableClock);

        var prefs = seeker.Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(enableClock.UtcNow);
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBe(disableClock.UtcNow);
    }

    [Fact]
    public void UpdateFollowedCompanyConsent_DisableFromNeverEnabled_DoesNotStampWithdrawnAt()
    {
        // No spurious revocation: disabling a consent that was never granted records nothing.
        var seeker = NewSeeker();

        seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, Later(1));

        seeker.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        seeker.Preferences.FollowedCompanyNotificationConsentAt.ShouldBeNull();
        seeker.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public void UpdateFollowedCompanyConsent_ReEnableAfterWithdrawal_ClearsWithdrawnAt_KeepsOriginalConsentAt()
    {
        var seeker = NewSeeker();
        var firstEnable = Later(1);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, firstEnable);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, Later(4));

        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(8));

        seeker.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        seeker.Preferences.FollowedCompanyNotificationConsentAt.ShouldBe(firstEnable.UtcNow);
        seeker.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Independence from the background-match consent + shared cadence
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateFollowedCompanyConsent_DoesNotAffectBackgroundMatchConsentOrCadence()
    {
        var seeker = NewSeeker();
        // Establish a background-match consent with a specific cadence first.
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, Later(1));

        // Toggling the company-follow consent must leave the background-match flag/timestamps and
        // the SHARED cadence untouched (ADR 0087 D2/D5).
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(2));

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.NotificationConsentAt.ShouldBe(BaseClock.UtcNow.AddHours(1));
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void UpdateNotificationConsent_DoesNotAffectFollowedCompanyConsent()
    {
        var seeker = NewSeeker();
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(1));

        // Toggling the background-match consent must not disturb the company-follow consent.
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, Later(2));
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, Later(3));

        seeker.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        seeker.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // AdvanceCompanyWatchScan — monotonic, clamp-to-now, independent watermark
    // ---------------------------------------------------------------

    [Fact]
    public void AdvanceCompanyWatchScan_FromNull_SetsWatermark()
    {
        var seeker = NewSeeker();
        var scanned = BaseClock.UtcNow.AddMinutes(-10);

        seeker.AdvanceCompanyWatchScan(scanned, Later(1));

        seeker.LastCompanyWatchScanAt.ShouldBe(scanned);
    }

    [Fact]
    public void AdvanceCompanyWatchScan_Backwards_IsIgnored()
    {
        var seeker = NewSeeker();
        var advanceClock = Later(2);
        seeker.AdvanceCompanyWatchScan(advanceClock.UtcNow, advanceClock);

        // An earlier scannedThrough must not rewind the watermark (monotonic).
        seeker.AdvanceCompanyWatchScan(advanceClock.UtcNow.AddHours(-1), Later(3));

        seeker.LastCompanyWatchScanAt.ShouldBe(advanceClock.UtcNow);
    }

    [Fact]
    public void AdvanceCompanyWatchScan_FutureScannedThrough_IsClampedToNow()
    {
        var seeker = NewSeeker();
        var nowClock = Later(2);

        seeker.AdvanceCompanyWatchScan(nowClock.UtcNow.AddDays(5), nowClock);

        seeker.LastCompanyWatchScanAt.ShouldBe(nowClock.UtcNow);
    }

    [Fact]
    public void AdvanceCompanyWatchScan_DoesNotAffectMatchScanWatermark()
    {
        var seeker = NewSeeker();
        var matchClock = Later(1);
        seeker.AdvanceMatchScan(matchClock.UtcNow, matchClock);

        seeker.AdvanceCompanyWatchScan(Later(2).UtcNow, Later(2));

        // The two watermarks are independent — advancing one leaves the other where it was.
        seeker.LastMatchScanAt.ShouldBe(matchClock.UtcNow);
        seeker.LastCompanyWatchScanAt.ShouldBe(BaseClock.UtcNow.AddHours(2));
    }
}
