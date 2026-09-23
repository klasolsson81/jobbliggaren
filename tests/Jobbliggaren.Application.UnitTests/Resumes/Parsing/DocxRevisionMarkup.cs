namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1803 — WordprocessingML tracked-change markup (ECMA-376 Part 1 §17.13.5), as the pieces of a hand-built
/// story. The attributes are there because producers write them; the extractor reads none of them.
/// </summary>
internal static class DocxRevisionMarkup
{
    private const string Stamp = " w:author=\"Granskare\" w:date=\"2026-09-23T00:00:00Z\"";

    public const string Pnr = "811218-9876";

    /// <summary>A paragraph mark deleted with tracked changes on, which joins the paragraph to the next.</summary>
    public const string DeletedMark = "<w:del w:id=\"90\"" + Stamp + "/>";

    /// <summary>The same mark written with an end tag.</summary>
    public const string DeletedMarkWithEndTag = "<w:del w:id=\"91\"" + Stamp + "></w:del>";

    public const string InsertedMark = "<w:ins w:id=\"92\"" + Stamp + "/>";

    public const string MovedMark = "<w:moveFrom w:id=\"93\"" + Stamp + "/>";

    public static string Para(params string[] content) => "<w:p>" + string.Concat(content) + "</w:p>";

    /// <summary>A paragraph whose mark carries <paramref name="marker"/> in its run properties.</summary>
    public static string MarkedPara(string marker, params string[] content) =>
        "<w:p><w:pPr><w:rPr>" + marker + "</w:rPr></w:pPr>" + string.Concat(content) + "</w:p>";

    /// <summary>A paragraph that defines a tab stop, so its properties hold a <c>w:tab</c> of their own.</summary>
    public static string TabStopPara(params string[] content) =>
        "<w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"720\"/></w:tabs></w:pPr>" + string.Concat(content) + "</w:p>";

    public static string Kept(string text) => "<w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r>";

    public static string DelText(string text) => "<w:r><w:delText xml:space=\"preserve\">" + text + "</w:delText></w:r>";

    public static string Del(params string[] runs) => "<w:del w:id=\"1\"" + Stamp + ">" + string.Concat(runs) + "</w:del>";

    public static string Ins(params string[] runs) => "<w:ins w:id=\"2\"" + Stamp + ">" + string.Concat(runs) + "</w:ins>";

    public static string MovedFrom(params string[] runs) =>
        "<w:moveFromRangeStart w:id=\"3\" w:name=\"flytt\"" + Stamp + "/>" +
        "<w:moveFrom w:id=\"4\"" + Stamp + ">" + string.Concat(runs) + "</w:moveFrom><w:moveFromRangeEnd w:id=\"3\"/>";

    public static string MovedTo(params string[] runs) =>
        "<w:moveToRangeStart w:id=\"5\" w:name=\"flytt\"" + Stamp + "/>" +
        "<w:moveTo w:id=\"6\"" + Stamp + ">" + string.Concat(runs) + "</w:moveTo><w:moveToRangeEnd w:id=\"5\"/>";

    /// <summary>An anchored DrawingML text box around <paramref name="paragraphs"/>, as run content.</summary>
    public static string TextBoxRun(params string[] paragraphs) =>
        "<w:r><w:drawing><wp:anchor><a:graphic><a:graphicData uri=\"urn:example\"><wps:wsp><wps:txbx><w:txbxContent>" +
        string.Concat(paragraphs) + "</w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>";
}
