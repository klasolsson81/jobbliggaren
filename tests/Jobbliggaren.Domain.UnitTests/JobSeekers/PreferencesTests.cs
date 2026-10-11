using System.Reflection;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

public class PreferencesTests
{
    private static readonly string[] RecordDefaultAllowlist =
        [nameof(Preferences.Language), nameof(Preferences.DigestCadence)];

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

    [Fact]
    public void Preferences_EveryConstructorDefaultOutsideTheAllowlist_IsTheClrDefaultOfItsType()
    {
        var parameters = typeof(Preferences).GetConstructors().Single().GetParameters();
        RecordDefaultAllowlist.ShouldBeSubsetOf(
            parameters.Select(parameter => parameter.Name),
            "an allowlisted name that is no longer a parameter is a stale exemption");

        var offenders = parameters
            .Where(parameter => !RecordDefaultAllowlist.Contains(parameter.Name) && !HasClrDefault(parameter))
            .Select(parameter => parameter.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            $"{string.Join(", ", offenders)}: a Preferences parameter must default to default(T) of its type. "
            + "A row written before that key existed has no such key in the preferences jsonb, and a missing key "
            + "reads as the CLR default, never as the record default, so a non-default record default is silently "
            + "ignored on every such row (ADR 0163; DigestCadence.cs:17-20). Choose a shape whose default(T) is the "
            + "intended value, as the nullable mail pause stamps do, where null means on.");
    }

    private static bool HasClrDefault(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
            return false;

        var value = parameter.DefaultValue;
        if (value is null)
            return true;

        var type = parameter.ParameterType;
        if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null)
            return false;

        return (type.IsEnum ? Enum.ToObject(type, value) : value).Equals(Activator.CreateInstance(type));
    }
}
