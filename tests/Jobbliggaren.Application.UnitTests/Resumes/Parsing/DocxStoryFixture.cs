using System.Text;
using DocumentFormat.OpenXml.Packaging;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1810 — a DOCX built with the OpenXml SDK: a main story and any of Word's other stories, each
/// given as its part's whole XML. Headers and footers are referenced from the section, cycling
/// default, first and even.
/// </summary>
internal static class DocxStoryFixture
{
    private const string Namespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    private static readonly string[] ReferenceTypes = ["default", "first", "even"];

    public static TheoryData<string> Stories() => ["header", "footer", "footnote", "endnote", "comment"];

    public static byte[] Build(string bodyXml, params (string Story, string PartXml)[] parts)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var references = new StringBuilder();
            var headers = 0;
            var footers = 0;
            foreach (var (story, partXml) in parts)
            {
                var part = AddPart(main, story);
                Feed(part, partXml);
                if (story == "header")
                    references.Append("<w:headerReference w:type=\"" + ReferenceTypes[headers++ % 3] + "\" r:id=\"" + main.GetIdOfPart(part) + "\"/>");
                else if (story == "footer")
                    references.Append("<w:footerReference w:type=\"" + ReferenceTypes[footers++ % 3] + "\" r:id=\"" + main.GetIdOfPart(part) + "\"/>");
            }

            Feed(main, "<w:document " + Namespaces + "><w:body>" + bodyXml +
                "<w:sectPr>" + references + "</w:sectPr></w:body></w:document>");
        }

        return stream.ToArray();
    }

    /// <summary>A story that holds one paragraph of text.</summary>
    public static (string Story, string PartXml) Paragraph(string story, string text) =>
        Part(story, "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>");

    public static (string Story, string PartXml) Part(string story, string content) => (story, Xml(story, content));

    /// <summary>A story's part XML: its root element around the given content.</summary>
    public static string Xml(string story, string content) => story switch
    {
        "header" => "<w:hdr " + Namespaces + ">" + content + "</w:hdr>",
        "footer" => "<w:ftr " + Namespaces + ">" + content + "</w:ftr>",
        "footnote" => "<w:footnotes " + Namespaces + "><w:footnote w:id=\"1\">" + content + "</w:footnote></w:footnotes>",
        "endnote" => "<w:endnotes " + Namespaces + "><w:endnote w:id=\"1\">" + content + "</w:endnote></w:endnotes>",
        "comment" => "<w:comments " + Namespaces + "><w:comment w:id=\"0\" w:author=\"Granskare\">" + content +
            "</w:comment></w:comments>",
        _ => throw new ArgumentOutOfRangeException(nameof(story), story, null),
    };

    private static OpenXmlPart AddPart(MainDocumentPart main, string story) => story switch
    {
        "header" => main.AddNewPart<HeaderPart>(),
        "footer" => main.AddNewPart<FooterPart>(),
        "footnote" => main.AddNewPart<FootnotesPart>(),
        "endnote" => main.AddNewPart<EndnotesPart>(),
        "comment" => main.AddNewPart<WordprocessingCommentsPart>(),
        _ => throw new ArgumentOutOfRangeException(nameof(story), story, null),
    };

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(data);
    }
}
