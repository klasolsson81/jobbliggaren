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
    public void UpdateNotificationConsent_CadenceChangeAfterWithdrawal_PreservesArticle7Timestamps()
    {
        // Bevakning F4 (#803): the digest cadence is SHARED with the followed-company
        // notifications (ADR 0087 D2), so the settings UI now lets a user change the
        // cadence while THIS consent is withdrawn — a call shape that was previously
        // unreachable (the cadence control was disabled when the flag was off). The
        // Art. 7 evidence trail must survive it: an already-withdrawn consent must not
        // be re-stamped with a fresh withdrawal time on every cadence save, and the
        // original consent time must not be cleared. Without this test, "simplifying"
        // the on->off guard into an unconditional `withdrawnAt = now` would silently
        // corrupt the accountability record while every other test stayed green.
        var seeker = NewSeeker();
        var consentAt = Later(1);
        var withdrawnAt = Later(4);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, consentAt);
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, withdrawnAt);

        // Two cadence saves while withdrawn — only the cadence may move.
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Daily, Later(9));
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, Later(12));

        var prefs = seeker.Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBe(consentAt.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBe(withdrawnAt.UtcNow);
        prefs.DigestCadence.ShouldBe(DigestCadence.Weekly);
    }

    [Fact]
    public void UpdateNotificationConsent_CadenceChangeWhileWithdrawn_LeavesFollowedCompanyConsentUntouched()
    {
        // The two purposes have separate flags and separate Art. 7 timestamp pairs.
        // A cadence write through the background-match command must not touch the
        // followed-company consent evidence (the shared cadence is the ONLY overlap).
        var seeker = NewSeeker();
        var followConsentAt = Later(1);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, followConsentAt);

        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Daily, Later(5));

        var prefs = seeker.Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(followConsentAt.UtcNow);
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
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
    public void SetLastSeenMatches_FromNull_SetsToSeenThrough()
    {
        var seeker = NewSeeker();
        seeker.LastSeenMatchesAt.ShouldBeNull();
        var clock = Later(1);

        seeker.SetLastSeenMatches(clock.UtcNow, clock);

        seeker.LastSeenMatchesAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void SetLastSeenMatches_WithLaterSeenThrough_Advances()
    {
        var seeker = NewSeeker();
        var firstClock = Later(1);
        seeker.SetLastSeenMatches(firstClock.UtcNow, firstClock);

        var laterClock = Later(5);
        seeker.SetLastSeenMatches(laterClock.UtcNow, laterClock);

        seeker.LastSeenMatchesAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SetLastSeenMatches_WithEarlierOrEqualSeenThrough_IsIgnored()
    {
        // Monotonic: a stale seen-through (e.g. a queued read processed out of order) must not
        // move the user-read watermark backwards (else the unread count would inflate).
        var seeker = NewSeeker();
        var current = Later(5);
        seeker.SetLastSeenMatches(current.UtcNow, current);

        var earlier = Later(1);
        seeker.SetLastSeenMatches(earlier.UtcNow, earlier); // earlier
        seeker.LastSeenMatchesAt.ShouldBe(current.UtcNow);

        var equal = Later(5);
        seeker.SetLastSeenMatches(equal.UtcNow, equal); // equal
        seeker.LastSeenMatchesAt.ShouldBe(current.UtcNow);
    }

    // #477 Low — the two-arg overload advances to the SEEN-THROUGH window (max CreatedAt the user
    // saw), NOT clock-now, so a match created between fetch and mark-seen stays flagged "nya".
    [Fact]
    public void SetLastSeenMatches_WithSeenThroughBeforeNow_AdvancesToSeenThroughNotNow()
    {
        var seeker = NewSeeker();
        var clock = Later(5);
        var seenThrough = clock.UtcNow.AddMinutes(-3); // newest match rendered, older than now

        seeker.SetLastSeenMatches(seenThrough, clock);

        seeker.LastSeenMatchesAt.ShouldBe(seenThrough);
        seeker.LastSeenMatchesAt.ShouldNotBe(clock.UtcNow, "the watermark is the seen window, not clock-now");
    }

    // A future-dated seenThrough (bad client clock) is clamped to now (mirrors AdvanceMatchScan).
    [Fact]
    public void SetLastSeenMatches_WithFutureSeenThrough_ClampsToNow()
    {
        var seeker = NewSeeker();
        var clock = Later(5);

        seeker.SetLastSeenMatches(clock.UtcNow.AddHours(1), clock);

        seeker.LastSeenMatchesAt.ShouldBe(clock.UtcNow);
    }

    // An equal seenThrough is a strict no-op — the <= guard must NOT bump UpdatedAt (kills the
    // <=→< boundary mutant: a duplicate mark-seen at the same watermark).
    [Fact]
    public void SetLastSeenMatches_AtEqualSeenThrough_IsNoOp_AndDoesNotBumpUpdatedAt()
    {
        var seeker = NewSeeker();
        var at = Later(5);
        seeker.SetLastSeenMatches(at.UtcNow, at);
        var updatedAfterFirst = seeker.UpdatedAt;

        seeker.SetLastSeenMatches(at.UtcNow, Later(9)); // same watermark value, later clock

        seeker.LastSeenMatchesAt.ShouldBe(at.UtcNow);
        seeker.UpdatedAt.ShouldBe(updatedAfterFirst, "equal watermark is a no-op — no spurious UpdatedAt churn");
    }
}
