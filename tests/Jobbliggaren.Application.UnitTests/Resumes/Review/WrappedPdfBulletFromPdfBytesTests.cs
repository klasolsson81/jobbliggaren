using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.WrappedPdfBulletTests;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #2342 — the measured wrap from PDF bytes, through the real extractor, segmenter and review
/// engine: the shape <see cref="WrappedPdfBulletTests"/> asserts on is one production emits.
/// </summary>
[Xunit.Collection("QuestPdfRendering")]
public class WrappedPdfBulletFromPdfBytesTests
{
    static WrappedPdfBulletFromPdfBytesTests() => QuestPDF.Settings.License = LicenseType.Community;

    // One text item per measured line, so the line ends are the measured ones and no layout
    // engine decides them. Small type keeps the longest line from wrapping again on the page.
    private static byte[] MeasuredEnglishCvPdf() =>
        Document.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(8));
                page.Content().Column(col =>
                {
                    foreach (var line in MeasuredEnglishCv.Split('\n'))
                        col.Item().Text(line);
                });
            })).GeneratePdf();

    [Fact]
    public async Task FromPdfBytes_AWrappedBulletIsOneUnit_AndNoCriterionCitesItsTail()
    {
        var extraction = new PdfPigOpenXmlCvTextExtractor()
            .Extract(MeasuredEnglishCvPdf(), CvFileKind.Pdf, TestContext.Current.CancellationToken);
        extraction.Status.ShouldBe(CvExtractionStatus.Extracted);
        extraction.RawText.Split('\n').ShouldBe(MeasuredEnglishCv.Split('\n'),
            "premise: the extractor ends each line where the measured document did");

        var parsed = ResumeFromCvText(extraction.RawText);

        Units(parsed).ShouldBe(MeasuredEnglishUnits);
        var result = await ReviewAsync(parsed);
        foreach (var criterionId in new[] { "A1", "A2", "A6" })
        {
            QuotesOf(result, criterionId).ShouldAllBe(s => MeasuredEnglishUnits.Contains(s.Quote));
        }
    }
}
