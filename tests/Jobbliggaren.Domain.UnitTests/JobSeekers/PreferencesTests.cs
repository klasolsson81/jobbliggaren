using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

public class PreferencesTests
{
    [Fact]
    public void Preferences_DefaultValues_AreCorrect()
    {
        var prefs = new Preferences();

        prefs.Language.ShouldBe("sv");
        // TD-115: EmailNotifications/WeeklySummary retired. The Vag 4 consent defaults
        // (the live notification model) stay OFF — the GDPR Art. 7 opt-in invariant.
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public void Preferences_ExplicitValues_ArePreserved()
    {
        var prefs = new Preferences(Language: "en", BackgroundMatchNotificationsEnabled: true);

        prefs.Language.ShouldBe("en");
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
    }
}
