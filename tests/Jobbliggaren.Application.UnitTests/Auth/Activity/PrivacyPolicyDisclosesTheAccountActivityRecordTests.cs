using System.Text.Json;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.Activity;

/// <summary>
/// #2161 (ADR 0172 D8) — the published privacy policy discloses the activity record in two items of its own, in two
/// sections: what is collected and on what basis, and how long it is kept. It states the minute precision the
/// recorder stores (<c>AccountActivityClaims.MinuteOf</c>, pinned in <see cref="AccountActivityClaimsTests"/>). The
/// items are found by their opening words rather than by index, because both sections are lists that other
/// processings append to.
/// </summary>
public class PrivacyPolicyDisclosesTheAccountActivityRecordTests
{
    [Theory]
    [InlineData("sv", "Senaste inloggning och användning. ", "på minuten", "så länge ditt konto finns")]
    [InlineData("en", "Last login and use. ", "to the minute", "for as long as your account exists")]
    public void The_record_is_disclosed_once_where_it_is_collected_and_once_where_it_is_kept(
        string language, string marker, string precision, string retention)
    {
        var items = PrivacyListItemsStartingWith(language, marker);

        items.Count.ShouldBe(2, $"{language}: one collection item and one retention item start with \"{marker}\"");
        items.Select(item => item.Section).Distinct().Count().ShouldBe(2);
        items.ShouldContain(item => item.Text.Contains(precision, StringComparison.Ordinal));
        items.ShouldContain(item => item.Text.Contains(retention, StringComparison.Ordinal));
    }

    private static List<(int Section, string Text)> PrivacyListItemsStartingWith(string language, string marker)
    {
        using var json = JsonDocument.Parse(ContentLegalMessages.ReadAllText(language));
        var found = new List<(int, string)>();
        var index = 0;
        foreach (var section in json.RootElement.GetProperty("privacy").GetProperty("sections").EnumerateArray())
        {
            if (section.TryGetProperty("list", out var list))
            {
                foreach (var item in list.EnumerateArray())
                {
                    var text = item.GetString();
                    if (text is not null && text.StartsWith(marker, StringComparison.Ordinal))
                        found.Add((index, text));
                }
            }
            index++;
        }
        return found;
    }
}
