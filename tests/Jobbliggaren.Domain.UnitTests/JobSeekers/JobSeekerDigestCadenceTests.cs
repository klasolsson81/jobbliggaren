using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// ADR 0087 D2 — <see cref="JobSeeker.SetDigestCadence"/> sets the cadence the two notification
/// consents share. A cadence write is neither consent's: it must leave both purposes' flags and Art. 7
/// timestamps exactly as they were, in every consent state.
/// </summary>
public class JobSeekerDigestCadenceTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    [Fact]
    public void SetDigestCadence_AfterWithdrawal_LeavesFlagAndBothArt7TimestampsUnchanged()
    {
        var seeker = NewSeeker();
        var consentAt = Later(1);
        var withdrawnAt = Later(4);
        seeker.UpdateNotificationConsent(enabled: true, consentAt);
        seeker.UpdateNotificationConsent(enabled: false, withdrawnAt);

        seeker.SetDigestCadence(DigestCadence.Daily, Later(9)).IsSuccess.ShouldBeTrue();

        var prefs = seeker.Preferences;
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBe(consentAt.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBe(withdrawnAt.UtcNow);
    }

    [Fact]
    public void SetDigestCadence_WhileConsented_LeavesFlagAndBothArt7TimestampsUnchanged()
    {
        var seeker = NewSeeker();
        var consentAt = Later(1);
        seeker.UpdateNotificationConsent(enabled: true, consentAt);

        seeker.SetDigestCadence(DigestCadence.Daily, Later(9)).IsSuccess.ShouldBeTrue();

        var prefs = seeker.Preferences;
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.NotificationConsentAt.ShouldBe(consentAt.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public void SetDigestCadence_NeverConsented_StampsNoArt7Record()
    {
        var seeker = NewSeeker();

        seeker.SetDigestCadence(DigestCadence.Daily, Later(9)).IsSuccess.ShouldBeTrue();

        var prefs = seeker.Preferences;
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public void SetDigestCadence_LeavesFollowedCompanyConsentUntouched()
    {
        // Withdrawn after a grant, so both of that purpose's timestamps are set and both are watched.
        var seeker = NewSeeker();
        var followConsentAt = Later(1);
        var followWithdrawnAt = Later(3);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, followConsentAt);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, followWithdrawnAt);

        seeker.SetDigestCadence(DigestCadence.Daily, Later(5)).IsSuccess.ShouldBeTrue();

        var prefs = seeker.Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(followConsentAt.UtcNow);
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBe(followWithdrawnAt.UtcNow);
    }

    [Fact]
    public void SetDigestCadence_NewValue_SetsCadenceAndBumpsUpdatedAt()
    {
        var seeker = NewSeeker();
        var clock = Later(2);

        seeker.SetDigestCadence(DigestCadence.Daily, clock).IsSuccess.ShouldBeTrue();

        seeker.Preferences.DigestCadence.ShouldBe(DigestCadence.Daily);
        seeker.UpdatedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void SetDigestCadence_SameValue_IsNoOp_AndDoesNotBumpUpdatedAt()
    {
        var seeker = NewSeeker();
        seeker.Preferences.DigestCadence.ShouldBe(DigestCadence.Weekly);
        var updatedBefore = seeker.UpdatedAt;
        var preferencesBefore = seeker.Preferences;

        seeker.SetDigestCadence(DigestCadence.Weekly, Later(5)).IsSuccess.ShouldBeTrue();

        seeker.Preferences.ShouldBeSameAs(preferencesBefore);
        seeker.UpdatedAt.ShouldBe(updatedBefore);
    }

    [Fact]
    public void SetDigestCadence_UndefinedValue_ReturnsValidationFailure_AndLeavesPreferencesUnchanged()
    {
        var seeker = NewSeeker();
        var updatedBefore = seeker.UpdatedAt;
        var preferencesBefore = seeker.Preferences;

        var result = seeker.SetDigestCadence((DigestCadence)99, Later(5));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DigestCadenceInvalid");
        seeker.Preferences.ShouldBeSameAs(preferencesBefore);
        seeker.UpdatedAt.ShouldBe(updatedBefore);
    }
}
