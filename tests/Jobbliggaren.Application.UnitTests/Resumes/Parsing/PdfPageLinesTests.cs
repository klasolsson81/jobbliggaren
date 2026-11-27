using System.Reflection;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;
using static Jobbliggaren.Infrastructure.Resumes.Parsing.PdfPageLines;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>#2353 D4: the mirror of PdfPig 0.1.16's GetText line breaks, and its alignment.</summary>
public class PdfPageLinesTests
{
    private static GlyphBox Glyph(char c, double x, double baseline) =>
        new(c.ToString(), x, x + 5, baseline, 11);

    private static IEnumerable<GlyphBox> Word(string word, double x, double baseline) =>
        word.Select((c, i) => Glyph(c, x + i * 5, baseline));

    [Fact]
    public void TheMirrorIsWrittenAgainstPdfPig0116()
    {
        // The line-break test is copied from ContentOrderTextExtractor at tag v0.1.16. A package bump
        // must re-read it before this test is changed, or the join silently stops measuring.
        var version = typeof(UglyToad.PdfPig.PdfDocument).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        version.ShouldStartWith("0.1.16");
    }

    [Fact]
    public void Align_GroupsLettersOnTwoBaselines_IntoTheTwoLinesOfTheText()
    {
        GlyphBox[] letters =
        [
            .. Word("led", 72, 700), Glyph(' ', 87, 700), .. Word("the", 92, 700),
            .. Word("team", 72, 686),
        ];

        var lines = Align(1, "led the\r\nteam", letters).ShouldNotBeNull();

        lines.Count.ShouldBe(2);
        lines[0].Words.Count.ShouldBe(2);
        lines[0].Left.ShouldBe(72);
        lines[0].Right.ShouldBe(107);
        lines[0].SpaceWidth.ShouldBe(5);
        lines[1].FirstWordWidth.ShouldBe(20);
        lines[1].SpaceWidth.ShouldBeNull();
    }

    [Fact]
    public void Align_IsNull_WhenTheTextHasAnotherLineCountThanTheLetters() =>
        Align(1, "led the team", [.. Word("led", 72, 700), .. Word("team", 72, 686)]).ShouldBeNull();

    [Fact]
    public void Align_IsNull_WhenALinesCharactersDifferFromItsLetters() =>
        Align(1, "led\nteem", [.. Word("led", 72, 700), .. Word("team", 72, 686)]).ShouldBeNull();
}
