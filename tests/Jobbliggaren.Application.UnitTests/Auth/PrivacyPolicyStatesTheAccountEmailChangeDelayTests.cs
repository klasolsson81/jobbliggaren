using System.Globalization;
using System.Text.Json;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1975 — the privacy policy's rectification item tells a person who has lost their account's inbox how long a
/// change an administrator starts waits (ADR 0153). The copy restates <see cref="AccountEmailChangePolicy.Delay"/>
/// and nothing keeps the two equal by construction, so the number is read out of the published copy, the way
/// <c>TermsAcceptanceVersionsMatchPublishedPolicyTests</c> reads the version. The published copy is the statement of
/// record: when this fails, fix whichever side is wrong deliberately.
/// </summary>
public class PrivacyPolicyStatesTheAccountEmailChangeDelayTests
{
    [Theory]
    [InlineData("sv", "tidigast {0} timmar efter")]
    [InlineData("en", "at the earliest {0} hours after")]
    public void The_rectification_item_states_the_delay_the_policy_enforces(string language, string form)
    {
        using var json = JsonDocument.Parse(ContentLegalMessages.ReadAllText(language));
        var rectification = json.RootElement.GetProperty("privacy").GetProperty("sections")[10]
            .GetProperty("list")[1].GetString();

        rectification.ShouldNotBeNull().ShouldContain(string.Format(
            CultureInfo.InvariantCulture, form, (int)AccountEmailChangePolicy.Delay.TotalHours));
    }
}
