using System.Text.Json;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The footer's tagline (DESIGN.md §11.5 point 3, #1825) against its home in the web app,
/// <c>messages/sv/landing.json</c> <c>brand.tagline</c>. The brand tagline (DESIGN.md §11) is the statement
/// of record: when the mirror fails, the mail follows it.
/// </summary>
public sealed class EmailFooterTaglineMirrorsBrandTaglineTests
{
    [Fact]
    public void Document_Footer_CarriesTheTaglineAndNoPriceLine()
    {
        var html = EmailHtml.Document("Rubrik", "Förhandsvisning", Markup.Empty);

        html.ShouldContain($"font-size:14px;line-height:1.5;color:{EmailHtml.Ink};\">{EmailHtml.Tagline}</div>");
        html.ShouldNotContain("gratis");
    }

    [Fact]
    public void Tagline_MatchesTheBrandTaglineTheWebAppPublishes()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(LandingMessagesPath()));

        document.RootElement
            .GetProperty("brand")
            .GetProperty("tagline")
            .GetString()
            .ShouldBe(EmailHtml.Tagline);
    }

    private static string LandingMessagesPath()
    {
        const string Relative = "web/jobbliggaren-web/messages/sv/landing.json";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, Relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {Relative} by walking up from {AppContext.BaseDirectory}. "
            + "The tagline pin needs the repo checkout to be present.");
    }
}
