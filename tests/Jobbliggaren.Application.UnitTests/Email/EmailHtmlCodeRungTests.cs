using System.Text.RegularExpressions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The code rung, DESIGN.md §11.5 point 3 (#1825). When a value here fails, DESIGN.md is the source and the
/// primitive follows it, never the other way round.
/// </summary>
public sealed class EmailHtmlCodeRungTests
{
    private static readonly Regex Style = new("style=\"([^\"]*)\"", RegexOptions.CultureInvariant);

    [Fact]
    public void Code_ForAOneTimeCode_RendersItAtTheCodeRungInAParagraphOfItsOwn()
    {
        var markup = EmailHtml.Code("042917").ToString();

        markup.ShouldStartWith("<p style=\"");
        markup.ShouldEndWith(">042917</p>");

        var rung = Declarations(markup);
        rung["font-size"].ShouldBe("28px");
        rung["line-height"].ShouldBe("1.2");
        rung["font-weight"].ShouldBe("700");
        rung["font-variant-numeric"].ShouldBe("tabular-nums");
        rung["letter-spacing"].ShouldBe("0.08em");
        rung["color"].ShouldBe(EmailHtml.Ink);
        rung["font-family"].ShouldBe(Declarations(EmailHtml.P("x").ToString())["font-family"]);
    }

    private static Dictionary<string, string> Declarations(string markup)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declaration in Style.Match(markup).Groups[1].Value
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            declarations[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
        }

        return declarations;
    }
}
