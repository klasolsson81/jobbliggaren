using System.Text;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;
using QuestDocument = QuestPDF.Fluent.Document;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2353 D4, from PDF bytes with real layout wraps (QuestPDF): the extractor joins a line end only
/// where the next word did not fit and nothing says the author ended the line. Each test asserts
/// its own premise against the unjoined text or the geometry, so a layout change that removes the
/// wrap fails it instead of passing it.
/// </summary>
[Xunit.Collection("QuestPdfRendering")]
public class PdfWrapJoinFromPdfBytesTests
{
    // Every word after the first is lower-case, so every wrap in it opens with a lower-case letter.
    private const string LowerCaseParagraph =
        "Reliable and detail oriented tester who cut the time from a reported defect to a verified fix by "
        + "introducing a shared triage routine with the developers and the support team in both of the offices";

    private readonly PdfPigOpenXmlCvTextExtractor _extractor = new();

    public PdfWrapJoinFromPdfBytesTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static byte[] Pdf(Action<ColumnDescriptor> content) =>
        QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(11));
                page.Content().Column(content);
            })).GeneratePdf();

    // Today's text before D4: GetText per page, pages joined by a line end, normalised.
    private static string UnjoinedText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (text.Length > 0)
                builder.Append(text).Append('\n');
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
    }

    private static List<PdfPageLines.PhysicalLine> Geometry(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var lines = new List<PdfPageLines.PhysicalLine>();
        foreach (var page in document.GetPages())
            lines.AddRange(PdfPageLines.Read(page, ContentOrderTextExtractor.GetText(page)).ShouldNotBeNull());

        return lines;
    }

    private CvExtractionResult Extract(byte[] pdf) =>
        _extractor.Extract(pdf, CvFileKind.Pdf, TestContext.Current.CancellationToken);

    [Fact]
    public void AWrappedParagraph_IsOneLine_WithOneSpaceAtEachSeam()
    {
        var pdf = Pdf(col => col.Item().Text(LowerCaseParagraph));
        UnjoinedText(pdf).ShouldNotContain(LowerCaseParagraph);

        var result = Extract(pdf);

        result.RawText.ShouldBe(LowerCaseParagraph);
        result.Provenance.LineCount.ShouldBe(1);
        result.Provenance[0].Joined.ShouldBeTrue();
    }

    [Fact]
    public void AnIndentedListWithoutGlyphsOrFinalStops_KeepsEachItemWhole_AndEachItemEnd()
    {
        // Her form: an indented list whose items carry no final stop, each wrapping onto lower-case.
        string[] items =
        [
            "Automated thirty regression tests together with the team, reducing the regression cycle from two days to four hours",
            "Designed a test strategy for the payment service together with three development teams and the owners of the product",
            "Mentored two junior testers",
        ];
        var pdf = Pdf(col =>
        {
            col.Item().Text(LowerCaseParagraph);
            foreach (var item in items)
                col.Item().PaddingLeft(18).Text(item);
        });
        UnjoinedText(pdf).ShouldNotContain(items[0]);
        UnjoinedText(pdf).ShouldNotContain(items[1]);

        var lines = Extract(pdf).RawText.Split('\n');

        lines.ShouldBe([LowerCaseParagraph, .. items]);
    }

    [Fact]
    public void AShortLineBeforeALowerCaseLine_IsTheAuthorsLineEnd()
    {
        var pdf = Pdf(col =>
        {
            col.Item().Text(LowerCaseParagraph);
            col.Item().Text("Mentored two junior testers");
            col.Item().Text("and wrote the onboarding guide");
        });

        Extract(pdf).RawText.Split('\n')[^2..].ShouldBe(["Mentored two junior testers", "and wrote the onboarding guide"]);
    }

    [Fact]
    public void AWrapAfterATerminator_IsNotJoined()
    {
        var sentence = string.Concat(Enumerable.Repeat("tested. built. reviewed. released. ", 12)).TrimEnd();
        var pdf = Pdf(col => col.Item().Text(sentence));
        UnjoinedText(pdf).Split('\n').Length.ShouldBeGreaterThan(1);

        Extract(pdf).RawText.ShouldBe(UnjoinedText(pdf));
    }

    [Fact]
    public void ALowerCaseLineAfterTheWidestLineOfAShortLastPage_IsNotJoined()
    {
        // Under a per-page edge, page 2's widest line would read as a wrap (senior-cto-advisor
        // 2026-11-27, point 1). The document's edge is page 1's.
        var pdf = Pdf(col =>
        {
            col.Item().Text(LowerCaseParagraph);
            col.Item().PageBreak();
            col.Item().Text("Selenium, Playwright and Jira");
            col.Item().Text("and more tools");
        });
        var geometry = Geometry(pdf);
        var pageTwo = geometry.Where(l => l.Page == 2).ToList();
        pageTwo.Count.ShouldBe(2);
        (pageTwo[0].Right + pageTwo[0].SpaceWidth!.Value + pageTwo[1].FirstWordWidth)
            .ShouldBeGreaterThan(pageTwo.Max(l => l.Right));

        Extract(pdf).RawText.Split('\n')[^2..].ShouldBe(["Selenium, Playwright and Jira", "and more tools"]);
    }

    [Fact]
    public void ALineOnTheNextPage_IsNeverJoined()
    {
        var pdf = Pdf(col =>
        {
            col.Item().Text(LowerCaseParagraph);
            col.Item().Text("and the testers from the support team who worked with the routine daily");
            col.Item().PageBreak();
            col.Item().Text("andcontinueswithaverylongfirstwordonthenextpage of text");
        });
        var geometry = Geometry(pdf);
        var last = geometry.Last(l => l.Page == 1);
        var next = geometry.First(l => l.Page == 2);
        PdfLineJoiner.IsWrap(last, "daily", next with { Page = last.Page }, "andcontinues", geometry.Max(l => l.Right))
            .ShouldBeTrue("the premise: only the page break separates them");

        Extract(pdf).RawText.Split('\n')[^1].ShouldBe("andcontinueswithaverylongfirstwordonthenextpage of text");
    }

    [Fact]
    public void ASidebarLine_NeverJoinsTheMainColumn()
    {
        // The main column is drawn first and sits right of the sidebar, so its last line runs to the
        // document's edge and the sidebar's first line follows it in the text.
        const string mainLastLine = "and the testers from the support team who worked with us all";
        var pdf = Pdf(col => col.Item().Layers(layers =>
        {
            layers.PrimaryLayer().PaddingLeft(160).Column(main =>
            {
                main.Item().Text(LowerCaseParagraph);
                main.Item().Text(mainLastLine);
            });
            layers.Layer().Width(140).Column(side =>
            {
                side.Item().Text("selenium and playwright");
                side.Item().Text("azure devops");
            });
        }));
        var geometry = Geometry(pdf);
        var lastMain = geometry[^3];
        var firstSide = geometry[^2];
        (lastMain.Right + lastMain.SpaceWidth!.Value + firstSide.FirstWordWidth)
            .ShouldBeGreaterThan(geometry.Max(l => l.Right), "the premise: the sidebar word would not have fitted");

        var lines = Extract(pdf).RawText.Split('\n');

        lines[^3..].ShouldBe([mainLastLine, "selenium and playwright", "azure devops"]);
    }

    [Fact]
    public void ThePersonnummerScan_FindsTheSameCandidates_InTheJoinedAndTheUnjoinedText()
    {
        // A join only ever replaces a line end before a lower-case letter, so it cannot glue digits.
        // The scan runs on the joined text (ImportResumeCommandHandler); this pins that it finds what
        // it found before D4.
        const string paragraph =
            "Reliable and detail oriented tester with the test number 811218-9876 written in the middle of a long line "
            + "that keeps going with lower case words until the layout has to wrap it onto the next line of the page";
        var pdf = Pdf(col => col.Item().Text(paragraph));
        var unjoined = UnjoinedText(pdf);
        var joined = Extract(pdf);
        joined.RawText.ShouldNotBe(unjoined);

        var before = Candidates(unjoined);

        before.ShouldNotBeEmpty();
        Candidates(joined.ScanText).ShouldBe(before);
    }

    private static List<(PersonnummerKind Kind, string Masked)> Candidates(string text) =>
        [.. PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .Select(m => (m.Kind, m.Masked))];

    [Fact]
    public async Task A9_ReadsASoftSkillSentenceWhole_WhenTheLayoutWrappedItBeforeItsExample()
    {
        // The interim false A9 Fail (senior-cto-advisor C): the soft skill and its number sat on two
        // lines of the extracted text. Joined, they are one sentence again (D3's test, closing #2350).
        const string profile =
            "Reliable tester with a strong interest in the craft who has been working for several years with the team, "
            + "and cut the time from a reported defect to a verified fix from 9 to 3 days.";
        var pdf = Pdf(col =>
        {
            col.Item().Text("Testa Testsson");
            col.Item().Text("testa@example.com");
            col.Item().Text("PROFILE");
            col.Item().Text(profile);
            col.Item().Text("EXPERIENCE");
            col.Item().Text("QA Engineer — Exempelbolaget AB");
            col.Item().Text("2021 – 2024");
            col.Item().Text("Designed a test strategy for the payment service.");
        });
        var unjoined = UnjoinedText(pdf).Split('\n');
        unjoined.ShouldNotContain(l => l.Contains("Reliable", StringComparison.Ordinal) && l.Contains("9 to 3", StringComparison.Ordinal));

        var raw = Extract(pdf).RawText;
        raw.Split('\n').ShouldContain(profile);
        var engine = new CvReviewEngine(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());
        var review = await engine.ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(raw)), RenderProfile.Ats, TestContext.Current.CancellationToken);

        VerdictOf(review, "A9").ShouldBe(CriterionVerdict.Pass);
    }
}
