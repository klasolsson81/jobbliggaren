using System.Globalization;
using System.Text.Json;
using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Feedback.Jobs;

/// <summary>
/// Pins <see cref="FeedbackRetentionJob.Retention"/> against the period the published privacy policy states
/// (#1979, ADR 0156). The period is a constant rather than an option for this reason: the policy names it, and
/// nothing keeps the two equal by construction. The published copy is the statement of record, so when this
/// fails, fix whichever side is wrong deliberately.
///
/// <para>
/// The item is found by its opening word rather than by index, because the retention section is a list that
/// other processings append to.
/// </para>
/// </summary>
public class PrivacyPolicyStatesTheFeedbackRetentionTests
{
    [Theory]
    [InlineData("sv", "Feedback. ", "{0} dagar efter att du skickade dem")]
    [InlineData("en", "Feedback. ", "{0} days after you sent them")]
    public void TheRetentionItem_StatesTheJobsPeriod(string language, string marker, string phrase)
    {
        var items = PrivacyListItemsStartingWith(language, marker);

        items.Count.ShouldBe(1, $"{language}: expected exactly one privacy list item starting with \"{marker}\"");
        items[0].ShouldContain(string.Format(CultureInfo.InvariantCulture, phrase, FeedbackRetentionJob.Retention.TotalDays));
    }

    [Fact]
    public void TheRetention_IsAWholeNumberOfDays()
    {
        // The copy states days; a period with a fractional day could never be stated truthfully.
        FeedbackRetentionJob.Retention.ShouldBe(TimeSpan.FromDays(Math.Floor(FeedbackRetentionJob.Retention.TotalDays)));
    }

    private static List<string> PrivacyListItemsStartingWith(string language, string marker)
    {
        using var json = JsonDocument.Parse(ContentLegalMessages.ReadAllText(language));
        var found = new List<string>();
        foreach (var section in json.RootElement.GetProperty("privacy").GetProperty("sections").EnumerateArray())
        {
            if (!section.TryGetProperty("list", out var list)) continue;
            foreach (var item in list.EnumerateArray())
            {
                var text = item.GetString();
                if (text is not null && text.StartsWith(marker, StringComparison.Ordinal)) found.Add(text);
            }
        }
        return found;
    }
}
