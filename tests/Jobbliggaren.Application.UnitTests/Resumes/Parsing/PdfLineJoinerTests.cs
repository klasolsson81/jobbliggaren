using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;
using static Jobbliggaren.Infrastructure.Resumes.Parsing.PdfPageLines;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2353 D4: the wrap rule on geometry alone. The PDF-bytes twins are in
/// <see cref="PdfWrapJoinFromPdfBytesTests"/>.
/// </summary>
public class PdfLineJoinerTests
{
    private const double Edge = 500;

    // A line of words starting at x, each word w wide with a gap of 3 between them.
    private static PhysicalLine Line(int page, double x, params double[] widths)
    {
        var words = new List<Word>();
        foreach (var width in widths)
        {
            words.Add(new Word(x, x + width));
            x += width + 3;
        }

        return new PhysicalLine(page, words);
    }

    // Right = 72 + 400 + 3 + 5 = 480 with a gap of 3, so a first word wider than 17 does not fit.
    private static PhysicalLine Full(int page = 1) => Line(page, 72, 400, 5);

    [Fact]
    public void AWordThatDidNotFit_BeforeALowerCaseLine_IsAWrap() =>
        PdfLineJoiner.IsWrap(Full(), "the time from a reported defect to a", Line(1, 72, 40, 20), "verified fix", Edge)
            .ShouldBeTrue();

    [Fact]
    public void AWordThatWouldHaveFitted_IsTheAuthorsLineEnd() =>
        PdfLineJoiner.IsWrap(Full(), "the time from a reported defect to a", Line(1, 72, 10, 20), "and more", Edge)
            .ShouldBeFalse();

    [Theory]
    [InlineData("the time from a reported defect to a", "Verified fix")]
    [InlineData("the time from a reported defect to a", "9 to 3 days")]
    [InlineData("the time from a reported defect.", "verified fix")]
    [InlineData("the time from a reported defect:", "verified fix")]
    [InlineData("the time from a reported defect…", "verified fix")]
    [InlineData("the time from a reported defect to a", "● verified fix")]
    [InlineData("the time from a reported defect to a", "- verified fix")]
    public void AWrapIsNotJoined_WhenTheTextSaysTheAuthorEndedTheLine(string previous, string next) =>
        PdfLineJoiner.IsWrap(Full(), previous, Line(1, 72, 40, 20), next, Edge).ShouldBeFalse();

    [Fact]
    public void LinesOnTwoPages_AreNeverJoined() =>
        PdfLineJoiner.IsWrap(Full(page: 1), "the time from a reported defect to a", Line(2, 72, 40, 20), "verified fix", Edge)
            .ShouldBeFalse();

    [Fact]
    public void LinesThatDoNotOverlap_AreNeverJoined() =>
        PdfLineJoiner.IsWrap(Line(1, 40, 100, 30), "a sidebar line", Line(1, 300, 230, 20), "verified fix", 200)
            .ShouldBeFalse();

    [Fact]
    public void AOneWordLine_HasNoSpaceToMeasure_AndIsNeverJoined() =>
        PdfLineJoiner.IsWrap(Line(1, 72, 420), "supercalifragilistic", Line(1, 72, 40, 20), "verified fix", Edge)
            .ShouldBeFalse();

    [Fact]
    public void Join_ReplacesOnlyTheJoinedSeam_AndRecordsTheFirstSegment()
    {
        const string text = "the time from a reported defect to a\nverified fix from nine to three days\nMentored two testers";
        var lines = new[] { Full(), Line(1, 72, 40, 20), Line(1, 72, 60, 20) };

        var joined = PdfLineJoiner.Join(text, lines).ShouldNotBeNull();

        joined.Text.ShouldBe(
            "the time from a reported defect to a verified fix from nine to three days\nMentored two testers");
        joined.Text.Length.ShouldBe(text.Length);
        joined.Provenance.LineCount.ShouldBe(2);
        joined.Provenance[0].Joined.ShouldBeTrue();
        joined.Provenance[0].FirstSegmentLength.ShouldBe("the time from a reported defect to a".Length);
        joined.Provenance[0].Left.ShouldBe(72);
        joined.Provenance[1].Joined.ShouldBeFalse();
        joined.Provenance[1].Measured.ShouldBeTrue();
    }

    [Fact]
    public void Join_MeasuresTheEdgeOnTheWholeDocument_NotOnAShortPage()
    {
        // Page 2's widest line ends at 300, far left of the document's edge (480), so a lower-case line
        // after it fitted and is the author's own line (senior-cto-advisor 2026-11-27, point 1).
        const string text = "the time from a reported defect to a\nskills and tools\nand more";
        var lines = new[] { Full(page: 1), Line(2, 72, 200, 25), Line(2, 72, 30, 30) };

        var joined = PdfLineJoiner.Join(text, lines).ShouldNotBeNull();

        joined.Text.ShouldBe(text);
        joined.Provenance[1].Joined.ShouldBeFalse();
    }

    // "● Handled tickets for …" with the glyph at 90 and the item's text at 108, ending at 480.
    private static PhysicalLine ItemLine() =>
        new(1, [new Word(90, 96), new Word(108, 470), new Word(473, 480)]);

    [Theory]
    [InlineData("● Handled tickets for the school network with about", "1,200 users")]
    [InlineData("● Handled tickets for the school network with about", "Twelve hundred users")]
    [InlineData("● Handled tickets for the school network.", "and closed them")]
    public void AWrapAtAListItemsTextIndent_ContinuesTheItem_WhateverItOpensWith(string previous, string next)
    {
        var item = PdfLineJoiner.ItemOf(ItemLine(), previous).ShouldNotBeNull();

        PdfLineJoiner.IsWrap(ItemLine(), previous, Line(1, 108, 40, 20), next, Edge, item).ShouldBeTrue();
    }

    [Fact]
    public void ALineAtTheGlyphsIndent_IsNotAContinuationOfTheItem()
    {
        var item = PdfLineJoiner.ItemOf(ItemLine(), "● Handled tickets for the school network with about").ShouldNotBeNull();

        PdfLineJoiner.IsWrap(ItemLine(), "● Handled tickets for the school network with about", Line(1, 90, 40, 20),
            "Twelve hundred users", Edge, item).ShouldBeFalse();
    }

    // A nested item whose glyph sits at its parent's text indent starts an item of its own, even
    // though its left edge is where a continuation of the parent would start and the parent's line
    // ends so near the edge that not even the glyph would have fitted.
    [Fact]
    public void ANestedItemWhoseGlyphSitsAtTheParentsTextIndent_IsNotAContinuation()
    {
        var fullItem = new PhysicalLine(1, [new Word(90, 96), new Word(108, 470), new Word(473, 495)]);
        var item = PdfLineJoiner.ItemOf(fullItem, "● Handled tickets for the school network with about").ShouldNotBeNull();

        PdfLineJoiner.IsWrap(fullItem, "● Handled tickets for the school network with about", Line(1, 108, 6, 60),
            "◦ escalations to the vendor", Edge, item).ShouldBeFalse();
    }

    [Fact]
    public void ALineWithoutALeadGlyph_StartsNoItem() =>
        PdfLineJoiner.ItemOf(Full(), "the time from a reported defect to a").ShouldBeNull();

    [Fact]
    public void Join_ContinuesAListItemAcrossADigitLedWrap_AndStartsTheNextItemAfresh()
    {
        const string text = "● Handled tickets for the school network with about\n1,200 users\n● Mentored two testers";
        var lines = new[] { ItemLine(), Line(1, 108, 30, 30), new PhysicalLine(1, [new Word(90, 96), new Word(108, 160)]) };

        var joined = PdfLineJoiner.Join(text, lines).ShouldNotBeNull();

        joined.Text.ShouldBe("● Handled tickets for the school network with about 1,200 users\n● Mentored two testers");
    }

    [Fact]
    public void Join_IsNull_WhenTheLinesDoNotIndexTheText() =>
        PdfLineJoiner.Join("one\ntwo", [Full()]).ShouldBeNull();
}
