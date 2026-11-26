using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2342 — the seam predicate that decides whether a line end in PDF text is a layout wrap.
/// One test per conjunct, each with the other conjuncts held true, so that dropping any one of
/// them reddens a test of its own (senior-cto-advisor D1, 2026-11-26).
/// </summary>
public class LineWrapMeasureTests
{
    private const string Docx = CvFileSignature.DocxContentType;

    // A raw text whose longest trimmed line is exactly `width` characters.
    private static string RawTextOfWidth(int width) => $"a\n{new string('x', width)}\nb";

    private static LineWrapMeasure MeasureOfWidth(int width) =>
        LineWrapMeasure.For(CvFileSignature.PdfContentType, RawTextOfWidth(width)).ShouldNotBeNull();

    [Theory]
    [InlineData(Docx)]
    [InlineData("application/octet-stream")]
    [InlineData("text/plain")]
    [InlineData("")]
    [InlineData(null)]
    public void For_ShouldReturnNoMeasure_WhenTheDeclaredTypeIsNotPdf(string? declaredContentType) =>
        LineWrapMeasure.For(declaredContentType, RawTextOfWidth(40)).ShouldBeNull();

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("Application/PDF")]
    [InlineData(" application/pdf ; charset=binary")]
    public void For_ShouldReturnAMeasure_WhenThePdfMediaTypeIsDeclared(string declaredContentType) =>
        LineWrapMeasure.For(declaredContentType, RawTextOfWidth(40)).ShouldNotBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t\n")]
    public void For_ShouldReturnNoMeasure_WhenTheRawTextHasNoText(string rawText) =>
        LineWrapMeasure.For(CvFileSignature.PdfContentType, rawText).ShouldBeNull();

    [Fact]
    public void For_ShouldTakeTheWidthFromTheLongestTrimmedLineOfTheWholeText()
    {
        var measure = LineWrapMeasure.For(CvFileSignature.PdfContentType, "abc\n   abcdefg   \n\tabcde\n")
            .ShouldNotBeNull();

        measure.Width.ShouldBe(7);
    }

    // The 2026-11-26 Chrome/Skia measurement (synthetic CV, headless Chrome, 11 pt Arial): the
    // bullet wrapped after "two", and the longest line of that document was 94 characters.
    [Fact]
    public void Continues_ShouldJoinTheMeasuredWrap()
    {
        var measure = MeasureOfWidth(94);

        measure.Continues(
                "Automated 35 regression tests with Selenium and C#, reducing the regression cycle from two",
                "days to four hours.")
            .ShouldBeTrue();
    }

    // p4 at its boundary: previous (7) + 1 + first token of next (3) = 11.
    [Fact]
    public void Continues_ShouldJoin_WhenTheNextWordWouldHaveEndedExactlyAtTheMeasure() =>
        MeasureOfWidth(11).Continues("abc def", "ghi jkl").ShouldBeTrue();

    [Fact]
    public void Continues_ShouldNotJoin_WhenTheNextWordWouldStillHaveFitBeforeTheMeasure() =>
        MeasureOfWidth(12).Continues("abc def", "ghi jkl").ShouldBeFalse();

    // p2: a line that ends a sentence or opens a list is not wrapped mid-clause.
    [Theory]
    [InlineData(".")]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData(":")]
    [InlineData(";")]
    [InlineData("…")]
    public void Continues_ShouldNotJoin_WhenThePreviousLineEndsWithATerminator(string terminator) =>
        MeasureOfWidth(10).Continues($"abc def{terminator}", "ghi jkl").ShouldBeFalse();

    [Fact]
    public void Continues_ShouldJoin_WhenThePreviousLineEndsWithAComma() =>
        MeasureOfWidth(10).Continues("abc def,", "ghi jkl").ShouldBeTrue();

    // p1: only a line opening with a lower-case letter can be the rest of a sentence. Each row is
    // a residual this rule accepts (R1): the wrap stays split.
    [Theory]
    [InlineData("Ghi jkl")]
    [InlineData("40 till 12")]
    [InlineData("• ghi jkl")]
    [InlineData("- ghi jkl")]
    [InlineData("(ghi jkl")]
    [InlineData("\"ghi jkl")]
    public void Continues_ShouldNotJoin_WhenTheNextLineDoesNotOpenWithALowerCaseLetter(string next) =>
        MeasureOfWidth(8).Continues("abc def", next).ShouldBeFalse();

    [Theory]
    [InlineData("", "ghi")]
    [InlineData("abc def", "")]
    public void Continues_ShouldNotJoinAnEmptyLine_ThatNoCallerCanPassIt(string previous, string next) =>
        LineWrapMeasure.For(CvFileSignature.PdfContentType, "abc def\nghi").ShouldNotBeNull()
            .Continues(previous, next).ShouldBeFalse();
}
