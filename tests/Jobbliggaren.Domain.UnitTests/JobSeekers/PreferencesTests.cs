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
        prefs.EmailNotifications.ShouldBeTrue();
        prefs.WeeklySummary.ShouldBeFalse();
    }

    [Fact]
    public void Preferences_ExplicitValues_ArePreserved()
    {
        var prefs = new Preferences(Language: "en", EmailNotifications: false, WeeklySummary: false);

        prefs.Language.ShouldBe("en");
        prefs.EmailNotifications.ShouldBeFalse();
        prefs.WeeklySummary.ShouldBeFalse();
    }
}
