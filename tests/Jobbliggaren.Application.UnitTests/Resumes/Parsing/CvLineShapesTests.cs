using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2353 finding 3: one owner of the list-item glyphs, the header separators and the header-field
/// reader (<see cref="CvLineShapes"/>). The segmenter, the entry-boundary reader and B5 read it.
/// </summary>
public class CvLineShapesTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = CvParsingLexiconFixture.Segmenter();

    [Theory]
    [InlineData('•')]
    [InlineData('◦')]
    [InlineData('‣')]
    [InlineData('·')]
    [InlineData('●')]
    [InlineData('○')]
    [InlineData('▪')]
    [InlineData('▫')]
    [InlineData('■')]
    [InlineData('–')]
    [InlineData('—')]
    [InlineData('-')]
    [InlineData('*')]
    public void IsMarkerLed_IsTrueForEveryListItemGlyph(char glyph)
    {
        CvLineShapes.IsMarkerLed($"  {glyph} Ledde teamet").ShouldBeTrue();
        CvLineShapes.MarkerGlyphList.ShouldContain(glyph);
    }

    [Fact]
    public void MarkerGlyphList_HoldsExactlyTheThirteenListItemGlyphs() =>
        CvLineShapes.MarkerGlyphList.Count.ShouldBe(13);

    [Theory]
    [InlineData("Ledde teamet")]
    [InlineData("2020 – 2023")]
    [InlineData("")]
    public void IsMarkerLed_IsFalseWhenTheLineOpensWithALetterOrDigit(string line) =>
        CvLineShapes.IsMarkerLed(line).ShouldBeFalse();

    [Theory]
    [InlineData("● Ledde teamet", true)]
    [InlineData("  • Ledde teamet", true)]
    [InlineData("*viktigt*", false)]
    [InlineData("-", false)]
    public void TryLeadMarker_NeedsWhitespaceAfterTheGlyph(string line, bool expected) =>
        CvLineShapes.TryLeadMarker(line, out _).ShouldBe(expected);

    [Theory]
    [InlineData(" — ", true)]
    [InlineData(" – ", true)]
    [InlineData(" - ", true)]
    [InlineData(" | ", true)]
    [InlineData(" @ ", true)]
    [InlineData(", ", false)]
    [InlineData(" at ", false)]
    [InlineData(" på ", false)]
    [InlineData(" hos ", false)]
    public void ReadsLowercaseHeader_OnlyOnASeparatorThatNeverJoinsProse(string separator, bool expected) =>
        CvLineShapes.ReadsLowercaseHeader(separator).ShouldBe(expected);

    [Fact]
    public void TryReadHeaderFields_SplitsOnTheFirstSeparatorInTableOrder()
    {
        CvLineShapes.TryReadHeaderFields("Utvecklare — Acme AB, Göteborg", out var title, out var organization, out var separator)
            .ShouldBeTrue();

        title.ShouldBe("Utvecklare");
        organization.ShouldBe("Acme AB, Göteborg");
        separator.ShouldBe(" — ");
    }

    [Fact]
    public void Segment_ASkillsListLedByListItemGlyphs_KeepsTheSkillsWithoutTheirGlyphs()
    {
        // Google Docs and Word put the list glyph in a PDF's text layer (● and •, measured 2026-11-27),
        // so a bulleted skills list reaches ParseList with its glyphs. The leading run is stripped once,
        // as before: "* - Docker" keeps its second glyph, because a space ends the run.
        var cv = string.Join('\n',
            "Erik Eriksson",
            "erik@example.com",
            "",
            "Kompetenser",
            "● Selenium",
            "▪ Playwright",
            "* - Docker");

        var result = _sut.Segment(cv);

        result.Content.Skills.ShouldBe(["Selenium", "Playwright", "- Docker"]);
    }
}
