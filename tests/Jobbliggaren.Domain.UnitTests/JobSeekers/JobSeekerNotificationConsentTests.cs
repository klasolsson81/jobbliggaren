using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// ADR 0080 Vag 4 PR-1 — the GDPR consent invariants on <see cref="JobSeeker"/>
/// (Art. 7(1) consent evidence stamped ONCE/immutable; Art. 7(3) withdrawal proof; no
/// spurious revocation) and the two monotonic per-user watermarks (LastMatchScanAt — the
/// Worker's scan high-water-mark, Beslut 2; LastSeenMatchesAt — the user-read watermark,
/// Beslut 6). The watermarks never move backwards.
/// </summary>
public class JobSeekerNotificationConsentTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(ValidUserId, "Klas Olsson", BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    // ---------------------------------------------------------------
    // UpdateNotificationConsent — Art. 7 consent lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateNotificationConsent_FirstEnable_StampsConsentAtAndSetsFlagAndCadence()
    {
        var seeker = NewSeeker();
        var enableClock = Later(1);

        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, enableClock);

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.NotificationConsentAt.ShouldBe(enableClock.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(enableClock.UtcNow);
    }

    [Fact]
    public void UpdateNotificationConsent_SecondEnable_DoesNotRestampConsentAt()
    {
        // Art. 7(1) evidence is immutable: the first-ever opt-in time is the consent record,
        // a later re-enable must not overwrite it.
        var seeker = NewSeeker();
        var firstEnable = Later(1);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, firstEnable);

        var secondEnable = Later(3);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, secondEnable);

        seeker.Preferences.NotificationConsentAt.ShouldBe(firstEnable.UtcNow);
        seeker.Preferences.NotificationConsentWithdrawnAt.ShouldBeNull();
        seeker.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void UpdateNotificationConsent_DisableFromEnabled_StampsWithdrawnAtAndKeepsConsentAt()
    {
        // Art. 7(3): the opt-out records a revocation time; the original consent evidence
        // (ConsentAt) is NOT erased — it remains as proof the consent once existed.
        var seeker = NewSeeker();
        var enableClock = Later(1);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, enableClock);

        var disableClock = Later(4);
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, disableClock);

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBe(enableClock.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBe(disableClock.UtcNow);
    }

    [Fact]
    public void UpdateNotificationConsent_ReEnableAfterWithdrawal_ClearsWithdrawnAtAndKeepsOriginalConsentAt()
    {
        var seeker = NewSeeker();
        var firstEnable = Later(1);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, firstEnable);
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, Later(4));

        var reEnable = Later(8);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, reEnable);

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
        prefs.NotificationConsentAt.ShouldBe(firstEnable.UtcNow);
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
    }

    [Fact]
    public void UpdateNotificationConsent_DisableWhenAlreadyDisabled_DoesNotStampWithdrawnAt()
    {
        // No spurious revocation: a fresh seeker has the flag OFF by default (never consented),
        // so a disable call must not fabricate an Art. 7(3) withdrawal record.
        var seeker = NewSeeker();
        seeker.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeFalse();

        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, Later(1));

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public void UpdateNotificationConsent_CadenceChangeWhileEnabled_Persists()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, Later(1));

        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, Later(2));

        seeker.Preferences.DigestCadence.ShouldBe(DigestCadence.Daily);
        seeker.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // AdvanceMatchScan — monotonic Worker scan high-water-mark (Beslut 2)
    // ---------------------------------------------------------------

    [Fact]
    public void AdvanceMatchScan_FromNull_SetsWatermark()
    {
        var seeker = NewSeeker();
        seeker.LastMatchScanAt.ShouldBeNull();
        var scannedThrough = BaseClock.UtcNow.AddMinutes(10);

        seeker.AdvanceMatchScan(scannedThrough, Later(1));

        seeker.LastMatchScanAt.ShouldBe(scannedThrough);
    }

    [Fact]
    public void AdvanceMatchScan_WithLaterTimestamp_Advances()
    {
        var seeker = NewSeeker();
        var first = BaseClock.UtcNow.AddMinutes(10);
        seeker.AdvanceMatchScan(first, Later(1));

        var second = BaseClock.UtcNow.AddMinutes(30);
        seeker.AdvanceMatchScan(second, Later(2));

        seeker.LastMatchScanAt.ShouldBe(second);
    }

    [Fact]
    public void AdvanceMatchScan_WithEarlierTimestamp_IsIgnored()
    {
        // Monotonic: a crash-retry that re-runs an earlier scan window must not move the
        // high-water-mark backwards (else matches would be re-scanned / re-notified).
        var seeker = NewSeeker();
        var current = BaseClock.UtcNow.AddMinutes(30);
        seeker.AdvanceMatchScan(current, Later(1));

        var earlier = BaseClock.UtcNow.AddMinutes(10);
        seeker.AdvanceMatchScan(earlier, Later(2));

        seeker.LastMatchScanAt.ShouldBe(current);
    }

    [Fact]
    public void AdvanceMatchScan_WithEqualTimestamp_IsIgnored()
    {
        var seeker = NewSeeker();
        var current = BaseClock.UtcNow.AddMinutes(30);
        seeker.AdvanceMatchScan(current, Later(1));

        seeker.AdvanceMatchScan(current, Later(2));

        seeker.LastMatchScanAt.ShouldBe(current);
    }

    // ---------------------------------------------------------------
    // SetLastSeenMatches — monotonic user-read watermark (Beslut 6)
    // ---------------------------------------------------------------

    [Fact]
    public void SetLastSeenMatches_FromNull_SetsToNow()
    {
        var seeker = NewSeeker();
        seeker.LastSeenMatchesAt.ShouldBeNull();
        var seenClock = Later(1);

        seeker.SetLastSeenMatches(seenClock);

        seeker.LastSeenMatchesAt.ShouldBe(seenClock.UtcNow);
    }

    [Fact]
    public void SetLastSeenMatches_WithLaterClock_Advances()
    {
        var seeker = NewSeeker();
        seeker.SetLastSeenMatches(Later(1));

        var laterClock = Later(5);
        seeker.SetLastSeenMatches(laterClock);

        seeker.LastSeenMatchesAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SetLastSeenMatches_WithEarlierOrEqualClock_IsIgnored()
    {
        // Monotonic: a stale clock (e.g. a queued read processed out of order) must not
        // move the user-read watermark backwards (else the unread count would inflate).
        var seeker = NewSeeker();
        var current = Later(5);
        seeker.SetLastSeenMatches(current);

        seeker.SetLastSeenMatches(Later(1)); // earlier
        seeker.LastSeenMatchesAt.ShouldBe(current.UtcNow);

        seeker.SetLastSeenMatches(Later(5)); // equal
        seeker.LastSeenMatchesAt.ShouldBe(current.UtcNow);
    }
}
