using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// The code rung, DESIGN.md §11.5 point 3 (#1825). When a value here fails, DESIGN.md is the source and the
/// primitive follows it, never the other way round.
/// </summary>
public sealed class EmailHtmlCodeRungTests
{
    [Fact]
    public void Code_ForAOneTimeCode_RendersItAtTheCodeRungInAParagraphOfItsOwn()
    {
        var markup = EmailHtml.Code("042917").ToString();

        markup.ShouldStartWith("<p style=\"");
        markup.ShouldEndWith(">042917</p>");
        foreach (var declaration in new[]
        {
            "font-size:28px", "line-height:1.2", "font-weight:700", "font-variant-numeric:tabular-nums",
            "letter-spacing:0.08em", $"color:{EmailHtml.Ink}",
        })
        {
            markup.ShouldContain(declaration);
        }
    }

    [Fact]
    public void Code_WhenTheTextCarriesMarkup_EncodesIt()
    {
        // The primitive's transform, asserted the way EmailHtml_WhenAValueWouldReachAnAttribute_EscapesTheQuote
        // asserts Button's.
        var markup = EmailHtml.Code("<b>042917</b>").ToString();

        markup.ShouldNotContain("<b>");
        markup.ShouldContain("&lt;b&gt;042917&lt;/b&gt;");
    }
}
