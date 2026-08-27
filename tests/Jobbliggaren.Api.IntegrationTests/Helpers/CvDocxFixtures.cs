using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

/// <summary>
/// Real in-memory DOCX CV fixtures for tests that need the import to reach a specific
/// <c>AutoPromoteOutcome</c> against the REAL extractor and segmenter. The 8-byte PDF stub used
/// elsewhere has no text layer, so it can only ever produce a degraded parse — which is exactly
/// why the Promoted arm has been unreachable from integration tests until now.
/// </summary>
public static class CvDocxFixtures
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// A minimal, valid in-memory DOCX (OpenXml) — identical construction to
    /// <c>PdfPigOpenXmlCvTextExtractorTests.BuildDocx</c>, so the real extractor yields the
    /// paragraphs back as raw text. The two private copies this assembly used to carry were
    /// retired in favour of this one.
    /// </summary>
    public static byte[] BuildDocx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
                body.AppendChild(new Paragraph(new Run(new Text(text))));
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A Swedish developer CV carrying the headings the heading-driven segmenter keys on, so the
    /// parse is confident enough for auto-promote and the deterministic derivers have both an
    /// education and an experience source to work from. Deliberately carries NO personnummer —
    /// the personnummer gate would otherwise block the promote before buildability is asked.
    /// </summary>
    public static byte[] ConfidentSwedishDeveloperCv() => BuildDocx(
        "Anna Andersson",
        "anna.andersson@example.com",
        "070-123 45 67",
        "Stockholm",
        "Profil",
        "Fullstack-utvecklare med flera års erfarenhet av .NET och webb.",
        "Arbetslivserfarenhet",
        "Systemutvecklare, Acme AB, 2021-2024",
        "Byggde och förvaltade webbtjänster i C# och ASP.NET Core.",
        "Backend-utvecklare, Contoso AB, 2018-2021",
        "Utvecklade API:er och integrationer.",
        "Utbildning",
        "Systemutvecklare .NET, NBI Handelsakademin, 2016-2018",
        "Kompetenser",
        "C#, .NET, ASP.NET Core, PostgreSQL, Entity Framework Core, React, TypeScript");
}
