using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;
using static Jobbliggaren.Domain.UnitTests.JobSeekers.ConsentLifecycleDriver;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// The two consent specifications over the states the aggregate's own methods produce (ADR 0080,
/// ADR 0087 D5): each follows its own consent through grant, withdrawal and a second grant, and
/// neither is satisfied by the other consent.
/// </summary>
public class NotificationConsentTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(BaseClock), BaseClock).Value;

    [Theory]
    [InlineData(Lifecycle.NeverGranted, false)]
    [InlineData(Lifecycle.Granted, true)]
    [InlineData(Lifecycle.Withdrawn, false)]
    [InlineData(Lifecycle.GrantedAgain, true)]
    public void BackgroundMatch_OverTheConsentLifecycle_IsSatisfiedOnlyWhileThatConsentIsGranted(
        Lifecycle lifecycle, bool granted)
    {
        var seeker = NewSeeker();
        Apply(lifecycle, seeker.UpdateNotificationConsent, fromHour: 1);

        NotificationConsent.BackgroundMatch.IsSatisfiedBy(seeker).ShouldBe(granted);
        NotificationConsent.FollowedCompany.IsSatisfiedBy(seeker).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Lifecycle.NeverGranted, false)]
    [InlineData(Lifecycle.Granted, true)]
    [InlineData(Lifecycle.Withdrawn, false)]
    [InlineData(Lifecycle.GrantedAgain, true)]
    public void FollowedCompany_OverTheConsentLifecycle_IsSatisfiedOnlyWhileThatConsentIsGranted(
        Lifecycle lifecycle, bool granted)
    {
        var seeker = NewSeeker();
        Apply(lifecycle, seeker.UpdateFollowedCompanyNotificationConsent, fromHour: 1);

        NotificationConsent.FollowedCompany.IsSatisfiedBy(seeker).ShouldBe(granted);
        NotificationConsent.BackgroundMatch.IsSatisfiedBy(seeker).ShouldBeFalse();
    }

    [Fact]
    public void BackgroundMatch_WithdrawnWhileFollowedCompanyStaysGranted_OnlyFollowedCompanyIsSatisfied()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, Later(1));
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(1));

        seeker.UpdateNotificationConsent(enabled: false, Later(2));

        NotificationConsent.BackgroundMatch.IsSatisfiedBy(seeker).ShouldBeFalse();
        NotificationConsent.FollowedCompany.IsSatisfiedBy(seeker).ShouldBeTrue();
    }

    [Fact]
    public void FollowedCompany_WithdrawnWhileBackgroundMatchStaysGranted_OnlyBackgroundMatchIsSatisfied()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, Later(1));
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(1));

        seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, Later(2));

        NotificationConsent.FollowedCompany.IsSatisfiedBy(seeker).ShouldBeFalse();
        NotificationConsent.BackgroundMatch.IsSatisfiedBy(seeker).ShouldBeTrue();
    }

    // Whether the account still exists is the JobSeeker query filter's to decide, not the consent's.
    [Fact]
    public void BothSpecifications_OnASoftDeletedAccount_KeepTheConsentItHad()
    {
        var seeker = NewSeeker();
        seeker.UpdateNotificationConsent(enabled: true, Later(1));
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, Later(1));

        seeker.SoftDelete(Later(2));

        NotificationConsent.BackgroundMatch.IsSatisfiedBy(seeker).ShouldBeTrue();
        NotificationConsent.FollowedCompany.IsSatisfiedBy(seeker).ShouldBeTrue();
    }

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));
}
