using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
using QuestDocument = QuestPDF.Fluent.Document;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// #2249 — entry boundaries recovered from each entry's own date when the text carries no blank
// line (PDF text always; a paragraph-spaced DOCX too), repeated typed sections kept apart, and the
// inline "Experience: …" heading accepted only when dated entries follow it.
//
// The fixture mirrors the STRUCTURE of the reported CV — eleven assignments under
// "Arbetslivserfarenhet", then two employer rows under "Anställningar" that state only the
// consultancy and the employment period — with invented people, clients and employers throughout.
// The reported files are personal data and are not part of this repository.
[Xunit.Collection("QuestPdfRendering")]
public class EntryBoundariesSegmentationTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = CvParsingLexiconFixture.Segmenter();
    private readonly PdfPigOpenXmlCvTextExtractor _extractor = new();

    static EntryBoundariesSegmentationTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private sealed record Item(string Role, string Organization, string Period, string[] Description);

    private static readonly Item[] Assignments =
    [
        new("Testledare prestandatest", "Exempelverket", "08/2025 – 05/2026",
            ["Planerade och genomförde last- och stresstester av provtjänsten.",
             "Analyserade resultaten och presenterade dem för leverantören.",
             "Teknik och arbetssätt: JMeter, SAML, JWT, distribuerade test."]),
        new("Testare / Testledare", "Strandmyndigheten", "08/2022 – 08/2025",
            ["Ansvarade för regressionstestning av två e-tjänster.",
             "Införde testautomatisering för formulärflöden.",
             "Teknik och arbetssätt: Playwright, Azure DevOps, WCAG."]),
        new("QA Manager", "Assistansbolaget Norr", "02/2025 – 07/2025",
            ["Ledde ett team med tre testare och etablerade teststrategin.",
             "Teknik och arbetssätt: Mikrotjänster, Jira, Kubernetes."]),
        new("Testledare / Scrum Master", "Arbetsgivarförbundet Väst", "06/2023 – 10/2024",
            ["Ansvarade för testledning inom Scrum för en ny webbplats,",
             "som lanserades i oktober 2024 efter större systemtester.",
             "Teknik och arbetssätt: Figma, Google Lighthouse, WCAG."]),
        new("Testledare", "Försäkringsföreningen Syd", "02/2023 – 12/2023",
            ["Var testledare och kravhanterare för en informationswebb.",
             "Teknik och arbetssätt: SharePoint, Telemetry, Optimizely CMS."]),
        new("Testledare / QA Manager", "Bostadsbolaget Öst", "11/2022 – 12/2024",
            ["Införde ett nattligt smoke-test av externwebbens sökfunktion.",
             "Teknik och arbetssätt: Episerver CMS, Jira, Azure DevOps."]),
        new("QA Manager", "Apoteksbolaget Mitt", "08/2022 – 01/2023",
            ["Testade inköpsflöden och behörigheter i receptsystemet.",
             "Teknik och arbetssätt: ERP, e-handel, Android, iOS."]),
        new("Senior QA-testare", "Spelstudion Uppsala", "11/2018 – 08/2022",
            ["Koordinerade dagliga smoke-tester i distribuerade team.",
             "Arbetade med lanseringar under 2019 och 2020 på flera plattformar.",
             "Teknik och arbetssätt: PlayFab, Postman, Confluence."]),
        new("Testledare", "Indiestudion Stockholm", "10/2016 – 11/2018",
            ["Genomförde användartester för två VR-projekt.",
             "Teknik och arbetssätt: Unity, Unreal Engine, UX."]),
        new("Testledare", "Kortspelsbolaget", "03/2016 – 09/2016",
            ["Samlade in användarfeedback för en prototyp.",
             "Teknik och arbetssätt: Postman, TDD, gamification."]),
        new("QA-trainee", "Konsultbyrån Exempel", "05/2015 – 02/2016",
            ["Skapade processmallar och introduktionsmaterial.",
             "Teknik och arbetssätt: Wordpress, ISO, QA."]),
    ];

    // The employer rows clarify WHICH consultancy and WHEN; the work itself is described under the
    // assignments, so these deliberately carry a single line (or none) of their own.
    private static readonly Item[] Employments =
    [
        new("Konsult / Testledare", "Konsultbolaget Alfa AB", "08/2025 – 11/2026 (nuvarande)",
            ["Arbetar med uppdrag inom kvalitetssäkring och QA-test."]),
        new("Testledare / Scrum Master", "Konsultbolaget Beta AB, Stockholm", "08/2022 – 07/2025", []),
    ];

    private static IEnumerable<string> ItemLines(Item item) =>
        [$"{item.Role} — {item.Organization}", item.Period, .. item.Description];

    /// <summary>The CV as text. <paramref name="blankBetweenEntries"/> false is what PDF extraction and
    /// a paragraph-spaced DOCX produce: not one blank line anywhere.</summary>
    private static List<string> CvLines(bool blankBetweenEntries)
    {
        var lines = new List<string> { "Anna Andersson", "Senior QA / Test Lead", "Profil",
            "Testledare med över tio års erfarenhet av test och kvalitetssäkring.", "Arbetslivserfarenhet" };

        void AddEntries(IEnumerable<Item> items)
        {
            foreach (var item in items)
            {
                if (blankBetweenEntries)
                    lines.Add(string.Empty);
                lines.AddRange(ItemLines(item));
            }

            if (blankBetweenEntries)
                lines.Add(string.Empty);
        }

        AddEntries(Assignments);
        lines.Add("Anställningar");
        AddEntries(Employments);
        lines.AddRange([
            "Utbildning", "Kandidatprogram i beteendevetenskap — Exempeluniversitetet", "2010 – 2013",
            "Certifikat", "• Professional Scrum Master I — Scrum.org, 2024",
            "Språkkunskaper", "Svenska — modersmål", "Engelska — flytande"]);
        return lines;
    }

    private static Item[] Expected => [.. Assignments, .. Employments];

    private static void ShouldMatchEveryItem(IReadOnlyList<ParsedExperience> experiences)
    {
        experiences.Count.ShouldBe(Expected.Length);
        for (var i = 0; i < Expected.Length; i++)
        {
            experiences[i].Title.ShouldBe(Expected[i].Role, $"entry {i}");
            experiences[i].Organization.ShouldBe(Expected[i].Organization, $"entry {i}");
            experiences[i].Period.ShouldNotBeNull($"entry {i}");
            Expected[i].Period.ShouldStartWith(experiences[i].Period!, Case.Sensitive, $"entry {i}");
            experiences[i].RawText.ShouldBe(string.Join('\n', ItemLines(Expected[i])), $"entry {i}");
        }
    }

    // ── The reported shape ──────────────────────────────────────────────────────────

    [Fact]
    public void Segment_AssignmentsAndEmploymentsWithoutBlankLines_YieldOneEntryEach()
    {
        var result = _sut.Segment(string.Join('\n', CvLines(blankBetweenEntries: false)));

        // 11 assignments + 2 employer rows. Before #2249: ONE entry holding all thirteen.
        ShouldMatchEveryItem(result.Content.Experience);
        result.Content.Education.Count.ShouldBe(1);
        result.Content.Languages.ShouldBe(["Svenska — modersmål", "Engelska — flytande"]);
    }

    [Fact]
    public void Segment_SameCvWithBlankLines_YieldsTheSameEntries()
    {
        var result = _sut.Segment(string.Join('\n', CvLines(blankBetweenEntries: true)));

        // Before #2249: twelve — the last assignment fused with the first employer row, because the
        // repeated Experience heading was joined with a single newline.
        ShouldMatchEveryItem(result.Content.Experience);
    }

    [Fact]
    public void Segment_RepeatedExperienceHeadings_KeepTheirBoundaryWhenTheFirstBlockHasNoTrailingBlank()
    {
        const string cv =
            """
            Arbetslivserfarenhet

            Testledare — Kund Ett AB
            2020 – 2021
            Testade betalflöden.

            Anställningar

            Konsult — Konsultbolaget Alfa AB
            2019 – 2022
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        experiences.Count.ShouldBe(2);
        experiences[0].RawText.ShouldBe("Testledare — Kund Ett AB\n2020 – 2021\nTestade betalflöden.");
        experiences[1].Organization.ShouldBe("Konsultbolaget Alfa AB");
        experiences[1].Period.ShouldBe("2019 – 2022");
    }

    [Fact]
    public void Segment_EmployerRowsWithoutDescriptions_EachStayAnEntryOfTheirOwn()
    {
        const string cv =
            """
            Anställningar
            Konsultbolaget Alfa AB
            2025 – nuvarande
            Konsultbolaget Beta AB
            2022 – 2025
            Spelbolaget Gamma AB
            2018 – 2022
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        experiences.Select(e => e.Title).ShouldBe(["Konsultbolaget Alfa AB", "Konsultbolaget Beta AB", "Spelbolaget Gamma AB"]);
        experiences.Select(e => e.Period).ShouldBe(["2025 – nuvarande", "2022 – 2025", "2018 – 2022"]);
    }

    // ── What must NOT become an entry ───────────────────────────────────────────────

    [Fact]
    public void Segment_YearsAndRangesInsideDescriptions_CreateNoEntry()
    {
        const string cv =
            """
            Arbetslivserfarenhet
            Testledare — Kund Ett AB
            2020 – 2023
            Webbplatsen lanserades i oktober 2024 efter tester under
            perioden 2021 – 2022
            • Pilotprojekt 2019 – 2020
            Migrerade det gamla systemet från 1998.
            Utvecklare — Kund Två AB
            2016 – 2020
            Byggde tjänster mellan 2017 och 2019.
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        // The lower-case continuation line ending in a range, the bulleted range and the bare years
        // are description; only the two authored date rows open an entry.
        experiences.Count.ShouldBe(2);
        experiences[0].Period.ShouldBe("2020 – 2023");
        experiences[0].RawText.ShouldContain("• Pilotprojekt 2019 – 2020");
        experiences[0].RawText.ShouldContain("Migrerade det gamla systemet från 1998.");
        experiences[1].Organization.ShouldBe("Kund Två AB");
    }

    [Fact]
    public void Segment_SingleDatedEntryWithoutBlankLines_IsUnchanged()
    {
        const string cv =
            """
            Arbetslivserfarenhet
            Systemutvecklare — Acme AB
            2019 – 2024
            Byggde integrationer.
            Ansvarade för releaser.
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        experiences.Count.ShouldBe(1);
        experiences[0].RawText.ShouldBe("Systemutvecklare — Acme AB\n2019 – 2024\nByggde integrationer.\nAnsvarade för releaser.");
    }

    // ── Header heights the document itself fixes ────────────────────────────────────

    [Fact]
    public void Segment_PeriodFirstEntriesWithoutBlankLines_SplitAtEachPeriod()
    {
        const string cv =
            """
            Arbetslivserfarenhet
            2019 – 2024
            Systemutvecklare — Acme AB
            Byggde integrationer.
            2015 – 2019
            Backend-utvecklare — Volvo Cars
            Byggde tjänster.
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        experiences.Select(e => e.Organization).ShouldBe(["Acme AB", "Volvo Cars"]);
        experiences.Select(e => e.Period).ShouldBe(["2019 – 2024", "2015 – 2019"]);
    }

    [Fact]
    public void Segment_ThreeLineHeadersWithoutBlankLines_KeepRoleAndEmployerTogether()
    {
        const string cv =
            """
            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            2019 – 2024
            Byggde integrationer.
            Backend-utvecklare
            Volvo Cars
            2015 – 2019
            Byggde tjänster.
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        experiences.Select(e => e.Title).ShouldBe(["Systemutvecklare", "Backend-utvecklare"]);
        experiences.Select(e => e.Organization).ShouldBe(["Acme AB", "Volvo Cars"]);
    }

    // ── Older English layouts: inline heading, dated header lines ───────────────────

    private const string CompactEnglishCv =
        """
        ALEX EXAMPLE
        QA consultant. Scrum master for more than 3 years.
        Senior QA professional with 10+ years of experience in agile software development.
        Experience: See LinkedIn for my education and recommendations.
        QA consultant Uppsala, Sweden Example Consulting AB 08/2025 - Present
        I performance tested web applications and APIs.
        Scrum Master Stockholm, Sweden Sample Experience AB 08/2022 - 07/2025
        I was responsible for several web QA projects during 2023-2024.
        • Automated tests for an e-service, released 2024 - 2025 across agencies.
        QA Tester Uppsala, Sweden Game Studio AB 11/2018 - 10/2021
        Tested the 2019 release on three platforms.
        """;

    [Fact]
    public void Segment_InlineExperienceHeadingFollowedByDatedHeaders_OpensTheSection()
    {
        var result = _sut.Segment(CompactEnglishCv);

        // Not at a blank-line boundary (PDF text never is), but a dated entry follows it.
        var experiences = result.Content.Experience;
        experiences.Count.ShouldBe(4);

        // The intro sentence the user wrote under the heading is kept verbatim, as its own entry —
        // never glued onto the first employer.
        experiences[0].RawText.ShouldBe("See LinkedIn for my education and recommendations.");
        experiences[0].Period.ShouldBeNull();

        experiences.Skip(1).Select(e => e.Period).ShouldBe(["08/2025 - Present", "08/2022 - 07/2025", "11/2018 - 10/2021"]);
        experiences[2].RawText.ShouldContain("• Automated tests for an e-service, released 2024 - 2025 across agencies.");
        result.Confidence.Sections.Single(s => s.Kind == ParsedSectionKind.Experience)
            .Level.ShouldBe(SectionConfidenceLevel.Confident);
    }

    [Fact]
    public void Segment_ProfileSentenceStartingWithExperienceColon_IsNotAHeading()
    {
        const string cv =
            """
            Profil
            Erfaren testledare med fokus på kvalitet.
            Experience: over 10 years in QA and test management.
            I enjoy coaching teams and improving delivery.
            Utbildning
            Civilingenjör — Exempeluniversitetet
            2005 – 2010
            """;

        var result = _sut.Segment(cv);

        result.Content.Experience.ShouldBeEmpty();
        result.Content.Profile.ShouldNotBeNull();
        result.Content.Profile.ShouldContain("Experience: over 10 years in QA and test management.");
        result.Confidence.Sections.Single(s => s.Kind == ParsedSectionKind.Experience)
            .Level.ShouldBe(SectionConfidenceLevel.NotFound);
    }

    [Fact]
    public void Segment_DatedHeadersWithoutAnyExperienceHeading_InventNoSection()
    {
        var cv = CompactEnglishCv.Replace("Experience: See LinkedIn", "Experience only. See LinkedIn", StringComparison.Ordinal);

        var result = _sut.Segment(cv);

        // The structure is there, the heading is not; the engine does not invent the section.
        result.Content.Experience.ShouldBeEmpty();
    }

    // ── From real bytes, through the production extractor ─────────────────────────

    [Fact]
    public void ExtractAndSegment_MultiPagePdfWithRunningHeader_YieldsTheSameEntriesAsTheText()
    {
        var lines = CvLines(blankBetweenEntries: false);
        var bytes = QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(11));
                page.Header().Text("Anna Andersson | CV 2026");
                page.Content().Column(col =>
                {
                    foreach (var line in lines)
                        col.Item().PaddingTop(line.Length < 30 ? 10 : 2).Text(line);
                });
            })).GeneratePdf();

        var extraction = _extractor.Extract(bytes, CvFileKind.Pdf, CancellationToken.None);

        extraction.Status.ShouldBe(CvExtractionStatus.Extracted);
        var text = extraction.RawText;
        text.Split('\n').ShouldAllBe(l => l.Trim().Length > 0, "PDF text carries no blank line");
        text.Split('\n').Count(l => l.Contains("CV 2026", StringComparison.Ordinal))
            .ShouldBeGreaterThan(1, "the fixture must span pages so the running header lands mid-section");

        var experiences = _sut.Segment(text).Content.Experience;

        experiences.Count.ShouldBe(Expected.Length);
        for (var i = 0; i < Expected.Length; i++)
        {
            experiences[i].Title.ShouldBe(Expected[i].Role, $"entry {i}");
            experiences[i].Organization.ShouldBe(Expected[i].Organization, $"entry {i}");
        }
    }

    [Fact]
    public void ExtractAndSegment_DocxWithParagraphSpacingInsteadOfEmptyParagraphs_YieldsOneEntryEach()
    {
        var bytes = BuildDocx(CvLines(blankBetweenEntries: false));

        var extraction = _extractor.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        ShouldMatchEveryItem(_sut.Segment(extraction.RawText).Content.Experience);
    }

    [Fact]
    public void ExtractAndSegment_DocxWithEmptyParagraphsBetweenEntries_YieldsOneEntryEach()
    {
        var bytes = BuildDocx(CvLines(blankBetweenEntries: true));

        var extraction = _extractor.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        ShouldMatchEveryItem(_sut.Segment(extraction.RawText).Content.Experience);
    }

    [Fact]
    public void ExtractAndSegment_TwoColumnPdf_KeepsSidebarOutOfEntriesAndEntriesInOrder()
    {
        string[] sidebar =
            ["Kontakt", "anna.andersson@example.com", "070-123 45 67", "Språk", "Svenska", "Engelska"];
        var main = new List<string> { "Arbetslivserfarenhet" };
        foreach (var item in Assignments.Take(4))
            main.AddRange(ItemLines(item));

        var bytes = QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));
                page.Content().Row(row =>
                {
                    row.ConstantItem(140).Column(col =>
                    {
                        foreach (var line in sidebar)
                            col.Item().Text(line);
                    });
                    row.RelativeItem().PaddingLeft(12).Column(col =>
                    {
                        foreach (var line in main)
                            col.Item().Text(line);
                    });
                });
            })).GeneratePdf();

        var text = _extractor.Extract(bytes, CvFileKind.Pdf, CancellationToken.None).RawText;
        var experiences = _sut.Segment(text).Content.Experience;

        experiences.Select(e => e.Organization).ShouldBe(Assignments.Take(4).Select(a => a.Organization));
        foreach (var experience in experiences)
        {
            foreach (var sidebarLine in sidebar)
                experience.RawText.ShouldNotContain(sidebarLine);
        }
    }

    private static byte[] BuildDocx(IEnumerable<string> lines)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var paragraphs = lines.Select(text => new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "160" }),
                new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(paragraphs));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
