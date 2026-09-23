using System.Net;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1740 (security-auditor Minor 7): every link the two notification mails build lands on a page the web
/// app serves.
///
/// <para>
/// The mails carry the Art. 7(3) withdrawal link, <c>{baseUrl}/mina-sidor</c>. The template tests pin that
/// literal and the web app pins its own routes, but nothing joined the two: renaming the page would break the
/// withdrawal link in every mail sent afterwards, with every suite green. The web app's route guard reads
/// only its own <c>src/</c>, so it cannot see this producer. Same move as
/// <see cref="ContactAddressMatchesPublishedContactTests"/>: a C# test reads the web app's files.
/// </para>
///
/// <para>
/// "Served" means a <c>page.tsx</c> under <c>src/app/(app)/</c> whose URL is the link's path. Route groups
/// add no URL segment, and parallel-route slots (<c>@modal</c>) and private folders serve no URL of their own.
/// A retired path that only answers with a 308 has no page, so a link to one fails here as well.
/// </para>
/// </summary>
public class NotificationMailLinksLandOnServedRoutesTests
{
    private const string BaseUrl = "https://jobbliggaren.se";

    private static readonly Regex HtmlLink = new(
        "href=\"(?<url>[^\"]+)\"", RegexOptions.CultureInvariant);

    private static readonly Regex TextLink = new(
        Regex.Escape(BaseUrl) + @"[^\s""<]*", RegexOptions.CultureInvariant);

    public static TheoryData<string> Mails => new() { "MatchNotification", "FollowedCompanyNotification" };

    [Theory]
    [MemberData(nameof(Mails))]
    public void EveryLinkInTheMail_IsAPageTheWebAppServes(string mail)
    {
        var served = ServedAppRoutes();
        var paths = LinkPaths(Render(mail));

        paths.ShouldNotBeEmpty(
            $"{mail} rendered no link to {BaseUrl}, so the sweep below would pass on nothing.");
        foreach (var path in paths)
        {
            served.ShouldContain(
                path,
                $"{mail} links to {BaseUrl}{path}, and no page.tsx under web/jobbliggaren-web/src/app/(app)/ "
                + "serves that path. A mail already sent keeps its link, so a renamed page needs a permanent "
                + "redirect as well as the new link.");
        }
    }

    /// <summary>
    /// Both mails in the form that renders every link they can carry: the followed-company mail links to
    /// <c>/foretag</c> only from its filter disclosure, so the summary is set.
    /// </summary>
    private static EmailTemplates.EmailContent Render(string mail) => mail switch
    {
        "MatchNotification" => EmailTemplates.MatchNotification(
            BaseUrl,
            new MatchNotificationEmail(
                MatchNotificationKind.Direct,
                Cadence: null,
                Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
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

    /// <summary>The path of every link to <see cref="BaseUrl"/> in either body, without query or fragment.</summary>
    private static HashSet<string> LinkPaths(EmailTemplates.EmailContent content)
    {
        var urls = HtmlLink.Matches(content.HtmlBody)
            .Select(m => WebUtility.HtmlDecode(m.Groups["url"].Value))
            .Concat(TextLink.Matches(content.PlainTextBody).Select(m => m.Value))
            .Where(url => url.StartsWith(BaseUrl + "/", StringComparison.Ordinal));

        return urls
            .Select(url => url[BaseUrl.Length..].Split('?', '#')[0])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The URL of every <c>page.tsx</c> under <c>src/app/(app)/</c>.</summary>
    private static HashSet<string> ServedAppRoutes()
    {
        var app = Path.Combine(FindWebApp(), "src", "app", "(app)");
        Directory.Exists(app).ShouldBeTrue($"expected the authenticated route group at {app}");

        var served = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in Directory.EnumerateFiles(app, "page.tsx", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(app, Path.GetDirectoryName(page)!)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s != ".")
                .ToList();

            if (segments.Any(s => s.StartsWith('@') || s.StartsWith('_'))) continue;

            served.Add("/" + string.Join('/', segments.Where(s => !s.StartsWith('('))));
        }

        return served;
    }

    private static string FindWebApp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "web", "jobbliggaren-web");
            if (Directory.Exists(Path.Combine(candidate, "src", "app"))) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find web/jobbliggaren-web/src/app by walking up from {AppContext.BaseDirectory}. "
            + "This test reads the web app's routes and needs the repo checkout to be present.");
    }
}
