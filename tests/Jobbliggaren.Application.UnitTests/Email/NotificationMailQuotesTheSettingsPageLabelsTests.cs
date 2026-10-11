using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

public class NotificationMailQuotesTheSettingsPageLabelsTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    private static readonly Regex Quoted = new("”(?<label>[^”]*)”", RegexOptions.CultureInvariant);

    public static TheoryData<string, string> MailsAndTheirSwitchLabelKeys => new()
    {
        { "MatchNotification", "backgroundMatch.topMatchLabel" },
        { "MatchNotificationDigest", "backgroundMatch.summaryLabel" },
        { "FollowedCompanyNotification", "followedCompanyNotifications.toggleLabel" },
    };

    [Theory]
    [MemberData(nameof(MailsAndTheirSwitchLabelKeys))]
    public void EveryLabelTheMailQuotes_IsItsSwitchLabelInTheSvSettingsCatalogue(string mail, string labelKey)
    {
        var content = Render(mail);
        var label = SvSettingsValue(labelKey);

        QuotedLabels(content.PlainTextBody).ShouldBe(new[] { label });
        QuotedLabels(WebUtility.HtmlDecode(content.HtmlBody)).ShouldBe(new[] { label });
    }

    private static List<string> QuotedLabels(string body) =>
        Quoted.Matches(body).Select(match => match.Groups["label"].Value).Distinct(StringComparer.Ordinal).ToList();

    private static EmailTemplates.EmailContent Render(string mail) => mail switch
    {
        "MatchNotification" => EmailTemplates.MatchNotification(
            BaseUrl,
            new MatchNotificationEmail(
                MatchNotificationKind.Direct,
                Cadence: null,
                Items: [new MatchNotificationItem(
                    Guid.Parse("0b7f3c1e-5a2d-4e8f-9c61-2d4a8e7b3f10"), "Backend-utvecklare", "Acme AB", "Toppmatch")],
                TotalCount: 1)),
        "MatchNotificationDigest" => EmailTemplates.MatchNotification(
            BaseUrl,
            new MatchNotificationEmail(
                MatchNotificationKind.Digest,
                DigestCadence.Weekly,
                Items: [new MatchNotificationItem(
                    Guid.Parse("0b7f3c1e-5a2d-4e8f-9c61-2d4a8e7b3f10"), "Backend-utvecklare", "Acme AB", "Stark match")],
                TotalCount: 1)),
        "FollowedCompanyNotification" => EmailTemplates.FollowedCompanyNotification(
            BaseUrl,
            new FollowedCompanyNotificationEmail(
                DigestCadence.Weekly,
                Items: [new FollowedCompanyAdItem("Backend-utvecklare", "Acme AB")],
                TotalCount: 1,
                FilterSummary: new FollowedCompanyFilterSummary(
                    OnlyMatchedActive: true, LocationFilterActive: true))),
        _ => throw new ArgumentOutOfRangeException(nameof(mail), mail, "no such mail in this test"),
    };

    private static string SvSettingsValue(string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SvSettingsPath()));
        var node = document.RootElement;
        foreach (var part in key.Split('.'))
            node = node.GetProperty(part);
        return node.GetString().ShouldNotBeNull();
    }

    private static string SvSettingsPath()
    {
        var relative = Path.Combine("web", "jobbliggaren-web", "messages", "sv", "settings.json");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relative} by walking up from {AppContext.BaseDirectory}.");
    }
}
