using System.Text;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.QA.Corpus.Layout;
using Shouldly;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// #2353 D4 over every PDF arm of the layout corpus. The extractor measures every page (the GetText
/// mirror aligns), and its text equals the unjoined text except where a seam's <c>'\n'</c> became
/// <c>' '</c>. No arm wraps a line today, so any join here would be a false one.
/// </summary>
public sealed class PdfWrapJoinCorpusTests
{
    public static TheoryData<string> PdfArms() =>
        [.. LayoutCaseCatalog.All.Where(c => c.ContentType == "application/pdf").Select(c => c.Id)];

    [Theory]
    [MemberData(nameof(PdfArms))]
    public void EveryPdfArm_IsMeasuredWhole_AndChangesOnlyAtJoinedSeams(string id)
    {
        var arm = LayoutCaseCatalog.All.Single(c => c.Id == id);
        var bytes = arm.Render(arm.Model);
        var unjoined = Unjoined(bytes);

        var result = new PdfPigOpenXmlCvTextExtractor().Extract(bytes, CvFileKind.Pdf, TestContext.Current.CancellationToken);

        var lineCount = result.RawText.Split('\n').Length;
        result.Provenance.LineCount.ShouldBe(lineCount);
        Enumerable.Range(0, lineCount).ShouldAllBe(line => result.Provenance[line].Measured);
        result.RawText.Length.ShouldBe(unjoined.Length);
        Enumerable.Range(0, unjoined.Length)
            .Where(i => result.RawText[i] != unjoined[i] && !(unjoined[i] == '\n' && result.RawText[i] == ' '))
            .ShouldBeEmpty();
        result.RawText.ShouldBe(unjoined);
    }

    // The extraction before D4: GetText per page, pages joined by a line end, normalised.
    private static string Unjoined(byte[] pdf)
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
}
