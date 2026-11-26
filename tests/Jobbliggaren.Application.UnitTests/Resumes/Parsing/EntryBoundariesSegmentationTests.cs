using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
using QuestDocument = QuestPDF.Fluent.Document;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// #2249 — entry boundaries recovered from each entry's own date when the text carries no blank
// line (PDF text always; a paragraph-spaced DOCX too), and repeated typed sections kept apart.
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
        new("Testkoordinator", "Exempelbolaget Ett AB", "07/2021 – 12/2021",
            ["Skrev testfall för ett påhittat bokningssystem.",
             "Sammanställde testrapporter för en fiktiv beställare.",
             "Teknik och arbetssätt: Exempelverktyg A, Exempelverktyg B."]),
        new("Mjukvarutestare / Testkoordinator", "Exempelbolaget Två AB", "01/2021 – 06/2021",
            ["Körde regressionstester av en fiktiv e-tjänst.",
             "Automatiserade flöden i ett påhittat formulär.",
             "Teknik och arbetssätt: Exempelverktyg C, Exempelverktyg D."]),
        new("Kvalitetsingenjör", "Exempelbolaget Tre AB", "07/2020 – 12/2020",
            ["Byggde upp en teststrategi för ett påhittat projekt.",
             "Teknik och arbetssätt: Exempelverktyg E, Exempelverktyg F."]),
        new("Testkoordinator / Agil coach", "Exempelbolaget Fyra AB", "01/2020 – 06/2020",
            ["Ledde testarbetet för en påhittad webbplats,",
             "som togs i drift i maj 2020 efter systemtester.",
             "Teknik och arbetssätt: Exempelverktyg G, Exempelverktyg H."]),
        new("Testkoordinator", "Exempelbolaget Fem AB", "07/2019 – 12/2019",
            ["Granskade krav för en fiktiv informationstjänst.",
             "Teknik och arbetssätt: Exempelverktyg I, Exempelverktyg J."]),
        new("Testkoordinator / Kvalitetsingenjör", "Exempelbolaget Sex AB", "01/2019 – 06/2019",
            ["Satte upp nattliga körningar mot en påhittad söktjänst.",
             "Teknik och arbetssätt: Exempelverktyg K, Exempelverktyg L."]),
        new("Kvalitetsingenjör", "Exempelbolaget Sju AB", "07/2018 – 12/2018",
            ["Testade behörigheter i ett fiktivt ordersystem.",
             "Teknik och arbetssätt: Exempelverktyg M, Exempelverktyg N."]),
        new("Senior mjukvarutestare", "Exempelbolaget Åtta AB", "01/2018 – 06/2018",
            ["Samordnade dagliga kontroller i ett påhittat team.",
             "Deltog i releaser under 2016 och 2017 på två plattformar.",
             "Teknik och arbetssätt: Exempelverktyg O, Exempelverktyg P."]),
        new("Testkoordinator", "Exempelbolaget Nio AB", "07/2017 – 12/2017",
            ["Höll användartester för ett fiktivt spel.",
             "Teknik och arbetssätt: Exempelverktyg Q, Exempelverktyg R."]),
        new("Testkoordinator", "Exempelbolaget Tio AB", "01/2017 – 06/2017",
            ["Samlade synpunkter på en påhittad prototyp.",
             "Teknik och arbetssätt: Exempelverktyg S, Exempelverktyg T."]),
        new("Testassistent", "Exempelbolaget Elva AB", "07/2016 – 12/2016",
            ["Skrev introduktionsmaterial för fiktiva kollegor.",
             "Teknik och arbetssätt: Exempelverktyg U, Exempelverktyg V."]),
    ];

    // The employer rows clarify WHICH consultancy and WHEN; the work itself is described under the
    // assignments, so these deliberately carry a single line (or none) of their own.
    private static readonly Item[] Employments =
    [
        new("Konsult / Testkoordinator", "Exempelkonsult Alfa AB", "01/2019 – 12/2021 (heltid)",
            ["Arbetar med påhittade uppdrag inom kvalitetssäkring."]),
        new("Konsult / Agil coach", "Exempelkonsult Beta AB, Exempelstad", "07/2016 – 12/2018", []),
    ];

    private static IEnumerable<string> ItemLines(Item item) =>
        [$"{item.Role} — {item.Organization}", item.Period, .. item.Description];

    /// <summary>The CV as text. <paramref name="blankBetweenEntries"/> false is what PDF extraction and
    /// a paragraph-spaced DOCX produce: not one blank line anywhere.</summary>
    private static List<string> CvLines(bool blankBetweenEntries)
    {
        var lines = new List<string> { "Anna Andersson", "Testkoordinator", "Profil",
            "Testkoordinator med påhittad erfarenhet av test och kvalitetssäkring.", "Arbetslivserfarenhet" };

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
            "Utbildning", "Kandidatprogram i exempelvetenskap — Exempeluniversitetet", "2012 – 2015",
            "Certifikat", "• Exempelcertifiering I — Exempelinstitutet, 2016",
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
    public void Segment_EmployerRowsNamingOnlyTheEmployer_StayFused()
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

        // Accepted and known: a header naming one field is not an anchor, so the run is not cut.
        experiences.Count.ShouldBe(1);
        experiences[0].RawText.ShouldBe(string.Join('\n', cv.ReplaceLineEndings("\n").Split('\n').Skip(1)));
    }

    // ── Counter-examples from the 2026-11-01 review of PR #2250 ─────────────────────

    // M1: a date inside ONE entry's description is not a second entry — in a blank-line CV (where
    // the blank lines stay the only boundary) and in the same CV without them.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Segment_DateRowInsideADescription_OpensNoEntry(bool blankBetweenEntries)
    {
        var experiences = _sut.Segment(Cv(blankBetweenEntries, "Arbetslivserfarenhet",
            ["Testledare — Acme AB", "2020 – 2023", "Certifierad ISTQB Foundation", "2021", "Ansvarade för regressionstester."],
            ["Utvecklare — Beta AB", "2016 – 2020", "Byggde tjänster."])).Content.Experience;

        experiences.Count.ShouldBe(2);
        experiences[0].RawText.ShouldBe(
            "Testledare — Acme AB\n2020 – 2023\nCertifierad ISTQB Foundation\n2021\nAnsvarade för regressionstester.");
        experiences[1].Organization.ShouldBe("Beta AB");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Segment_DatedLineWithoutAnEmployerInsideADescription_OpensNoEntry(bool blankBetweenEntries)
    {
        var experiences = _sut.Segment(Cv(blankBetweenEntries, "Arbetslivserfarenhet",
            ["Konsult — Acme AB 2019 – 2024", "Projektledning för molnmigrering 2021 – 2022", "Ansvarade för releaser."],
            ["Utvecklare — Beta AB 2015 – 2019", "Byggde tjänster."])).Content.Experience;

        experiences.Count.ShouldBe(2);
        experiences[0].RawText.ShouldBe(
            "Konsult — Acme AB 2019 – 2024\nProjektledning för molnmigrering 2021 – 2022\nAnsvarade för releaser.");
        experiences[1].Organization.ShouldBe("Beta AB");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Segment_YearRowInsideAnEducationEntry_OpensNoEntry(bool blankBetweenEntries)
    {
        var educations = _sut.Segment(Cv(blankBetweenEntries, "Utbildning",
            ["Civilingenjör — Exempeluniversitetet", "2005 – 2010", "Examensarbete inom maskininlärning", "2010"],
            ["Gymnasieexamen — Exempelgymnasiet", "2002 – 2005"])).Content.Education;

        educations.Count.ShouldBe(2);
        educations[0].RawText.ShouldBe(
            "Civilingenjör — Exempeluniversitetet\n2005 – 2010\nExamensarbete inom maskininlärning\n2010");
        educations[1].Institution.ShouldBe("Exempelgymnasiet");
    }

    // M2: a header that starts in lower case still anchors on a dash, and no later description
    // line becomes a title or an employer.
    [Fact]
    public void Segment_LowercaseInitialHeaders_KeepEveryEntryWhole()
    {
        var experiences = _sut.Segment(Cv(false, "Arbetslivserfarenhet",
            ["iOS-utvecklare — Acme AB", "2020 – 2023", "Byggde appar, ansvarade för releaser."],
            ["e-handelsansvarig — Beta AB", "2018 – 2020", "Byggde tjänster, skrev tester."],
            ["Utvecklare — Gamma AB", "2015 – 2018", "Ritade system."])).Content.Experience;

        experiences.Select(e => e.Title).ShouldBe(["iOS-utvecklare", "e-handelsansvarig", "Utvecklare"]);
        experiences.Select(e => e.Organization).ShouldBe(["Acme AB", "Beta AB", "Gamma AB"]);
    }

    // A lower-case line split only on a comma reads as a wrapped description clause, so it never
    // heads the date row under it.
    [Fact]
    public void Segment_LowercaseCommaLineAboveADateRow_IsNoHeader()
    {
        var experiences = _sut.Segment(Cv(false, "Arbetslivserfarenhet",
            ["Testledare — Acme AB", "2020 – 2023", "Ledde testarbetet för två system och", "byggde appar, skrev tester", "2018 – 2019"],
            ["Utvecklare — Beta AB", "2015 – 2018", "Ritade system."])).Content.Experience;

        experiences.Select(e => e.Title).ShouldBe(["Testledare", "Utvecklare"]);
        experiences[0].RawText.ShouldEndWith("byggde appar, skrev tester\n2018 – 2019");
    }

    [Fact]
    public void Segment_SubAssignmentInsideAnEntryOfABlankLineSeparatedSection_StaysInThatEntry()
    {
        var experiences = _sut.Segment(Cv(true, "Arbetslivserfarenhet",
            ["Konsult — Acme AB", "2019 – 2024", "Uppdrag i urval.", "Projektledare, Volvo", "2021 – 2022", "Ledde migreringen."],
            ["Utvecklare — Beta AB", "2015 – 2019", "Byggde tjänster."])).Content.Experience;

        experiences.Count.ShouldBe(2);
        experiences[0].RawText.ShouldBe(
            "Konsult — Acme AB\n2019 – 2024\nUppdrag i urval.\nProjektledare, Volvo\n2021 – 2022\nLedde migreringen.");
    }

    // Accepted and known: in a section holding one blank-line-free entry, a sub-assignment that
    // names a role and an employer over a range is shaped exactly like an entry, and is cut.
    [Fact]
    public void Segment_SubAssignmentInsideTheOnlyEntryOfASection_IsCut()
    {
        var experiences = _sut.Segment(Cv(true, "Arbetslivserfarenhet",
            ["Konsult — Acme AB", "2019 – 2024", "Uppdrag i urval.", "Projektledare, Volvo", "2021 – 2022", "Ledde migreringen."]))
            .Content.Experience;

        experiences.Select(e => e.Organization).ShouldBe(["Acme AB", "Volvo"]);
    }

    // A later entry written in another layout than the first is read in neither: "Utvecklare" /
    // "Beta AB, Stockholm" over a range is a two-line header and an intro over a one-line header
    // alike, so no title or organization is read off it.
    [Theory]
    [InlineData("Testledare — Acme AB|2020 – 2023|Testade betalflöden.|Utvecklare|Beta AB, Stockholm|2016 – 2020|Byggde tjänster.")]
    [InlineData("Testledare — Acme AB 2020 – 2023|Testade betalflöden.|Utvecklare|Beta AB, Stockholm 2016 – 2020|Byggde tjänster.")]
    public void Segment_LaterEntryInAnotherLayout_IsNotCut(string entryLines)
    {
        var experiences = _sut.Segment(Cv(false, "Arbetslivserfarenhet", entryLines.Split('|'))).Content.Experience;

        experiences.Count.ShouldBe(1);
        experiences[0].Organization.ShouldBe("Acme AB");
    }

    // A dated line whose text before the date ends a sentence is description, never a dated header.
    [Fact]
    public void Segment_DatedSentenceInsideADescription_OpensNoEntry()
    {
        var experiences = _sut.Segment(
            """
            Arbetslivserfarenhet
            Testledare — Acme AB 2020 – 2023
            Testade betalflöden.
            Vikarierade hos Volvo. 2019 – 2020
            Studieuppehåll, resa i Asien. 2018 – 2019
            Utvecklare — Beta AB 2016 – 2018
            Byggde tjänster.
            """).Content.Experience;

        experiences.Select(e => e.Organization).ShouldBe(["Acme AB", "Beta AB"]);
    }

    // In a period-first run, a range whose line above is header-shaped reads as that line's date too,
    // so it is not cut.
    [Fact]
    public void Segment_PeriodFirstRunWithALaterHeaderAboveItsRange_IsNotCut()
    {
        var experiences = _sut.Segment(
            """
            Arbetslivserfarenhet
            2020 – 2023
            Testledare — Acme AB
            Testade betalflöden.
            Utvecklare — Beta AB
            2016 – 2020
            Byggde tjänster, skrev tester
            """).Content.Experience;

        experiences.Count.ShouldBe(1);
        experiences[0].Organization.ShouldBe("Acme AB");
    }

    // A header whose second field is only a year names no employer: the anchor test and the field
    // split read the line alike, so no entry with an empty organization or institution is cut.
    [Fact]
    public void Segment_HeaderWhoseSecondFieldIsAYear_OpensNoEntry()
    {
        var content = _sut.Segment(
            """
            Arbetslivserfarenhet
            Testledare — Acme AB
            2020 – 2023
            Testade betalflöden.
            Praktikant, 2019
            2019 – 2019
            Utvecklare — Beta AB
            2016 – 2019
            Utbildning
            Civilingenjör — Exempeluniversitetet
            2005 – 2010
            Naturvetenskapsprogrammet, 2005
            2002 – 2005
            """).Content;

        content.Experience.Select(e => e.Organization).ShouldBe(["Acme AB", "Beta AB"]);
        content.Education.Count.ShouldBe(1);
        content.Education[0].Institution.ShouldBe("Exempeluniversitetet");
    }

    // M3: an unpunctuated line above the first header reads both as an intro and as the role of a
    // two-line header, so the run is not cut; a punctuated intro is unambiguous and is cut.
    [Fact]
    public void Segment_UnpunctuatedIntroAboveTheFirstHeader_StaysFused()
    {
        var experiences = _sut.Segment(Cv(false, "Arbetslivserfarenhet",
            ["Konsultuppdrag via Konsultbolaget Alfa AB"],
            ["Testledare — Kund Ett AB", "2020 – 2023", "Teknik: Playwright, Azure DevOps"],
            ["Utvecklare — Kund Två AB", "2018 – 2020", "Teknik: Java, Spring"])).Content.Experience;

        experiences.Count.ShouldBe(1);
        experiences.ShouldNotContain(e => e.Title != null && e.Title.StartsWith("Teknik", StringComparison.Ordinal));
    }

    [Fact]
    public void Segment_PunctuatedIntroAboveTheFirstHeader_IsRoutedAndTheEntriesAreCut()
    {
        var content = _sut.Segment(Cv(false, "Arbetslivserfarenhet",
            ["Konsultuppdrag via Konsultbolaget Alfa AB."],
            ["Testledare — Kund Ett AB", "2020 – 2023", "Teknik: Playwright, Azure DevOps"],
            ["Utvecklare — Kund Två AB", "2018 – 2020", "Teknik: Java, Spring"])).Content;

        content.Experience.Select(e => e.Title).ShouldBe(["Testledare", "Utvecklare"]);
        content.Experience.Select(e => e.Organization).ShouldBe(["Kund Ett AB", "Kund Två AB"]);
        content.Experience[0].RawText.ShouldEndWith("Teknik: Playwright, Azure DevOps");
        content.Sections.Single(s => s.Heading == "Arbetslivserfarenhet").Entries
            .Single().Lines.ShouldBe(["Konsultuppdrag via Konsultbolaget Alfa AB."]);
    }

    // M4: the intro line under the heading becomes no entry, so no company-less entry blocks the
    // whole CV at the auto-promote gate.
    [Fact]
    public void Segment_IntroLineAboveDatedHeaders_IsRoutedAndTheCvStaysPromotable()
    {
        const string cv =
            """
            Alex Example
            alex@example.com
            Experience
            See the attached portfolio for references.
            Example Role One — Example Company One AB 01/2021 - Present
            I tested a fictional booking service.
            Example Role Two — Example Company Two AB 01/2019 - 12/2020
            I ran tests for a fictional web shop.
            Example Role Three — Example Company Three AB 01/2017 - 12/2018
            Tested a fictional release on two platforms.
            """;

        var result = _sut.Segment(cv);

        result.Content.Experience.Select(e => e.Organization)
            .ShouldBe(["Example Company One AB", "Example Company Two AB", "Example Company Three AB"]);
        result.Content.Sections.Single(s => s.Heading == "Experience").Entries
            .Single().Lines.ShouldBe(["See the attached portfolio for references."]);

        var parsed = ParsedResume.Create(
            JobSeekerId.New(), "cv.pdf", "application/pdf", result.DetectedLanguage, result.Content, cv,
            result.Confidence, PersonnummerScanOutcome.None, [], FakeDateTimeProvider.Default).Value;
        AutoPromoteGate.Evaluate(parsed, "CV", parsed.JobSeekerId, FakeDateTimeProvider.Default)
            .ShouldBeOfType<AutoPromoteGateVerdict.Promotable>();
    }

    // m6: a running page header carrying the person's name, between a role line and its date row,
    // is never read as a header. Accepted and known: that entry stays fused with the one above.
    [Fact]
    public void Segment_RunningHeaderWithTheNameBetweenRoleAndDate_IsNeverAHeader()
    {
        const string cv =
            """
            Anna Andersson
            anna@example.com
            Arbetslivserfarenhet
            Testledare — Kund Ett AB
            2020 – 2023
            Testade betalflöden.
            Utvecklare — Kund Två AB
            Anna Andersson | CV 2026
            2016 – 2020
            Byggde tjänster.
            """;

        var experiences = _sut.Segment(cv).Content.Experience;

        experiences.Count.ShouldBe(1);
        experiences[0].Title.ShouldBe("Testledare");
    }

    // Accepted and known: "Role" / "Employer, City" reads as a two-line header and as an intro over a
    // one-line header alike, so the run is not cut.
    [Fact]
    public void Segment_TwoLineHeaderWhoseEmployerLineCarriesACity_StaysFused()
    {
        var experiences = _sut.Segment(Cv(false, "Arbetslivserfarenhet",
            ["Systemutvecklare", "Acme AB, Göteborg", "2019 – 2024", "Byggde integrationer."],
            ["Backend-utvecklare", "Volvo Cars, Torslanda", "2015 – 2019", "Byggde tjänster."])).Content.Experience;

        experiences.Count.ShouldBe(1);
    }

    // Accepted and known: a dated line above the first anchor is an entry the rule could not read,
    // and it is not filed as free text outside the section.
    [Fact]
    public void Segment_DatedLineAboveTheFirstAnchor_StaysFused()
    {
        var content = _sut.Segment(Cv(false, "Arbetslivserfarenhet",
            ["Sommarjobb på Exempelbolaget.", "2014"],
            ["Testledare — Kund Ett AB", "2020 – 2023", "Testade betalflöden."],
            ["Utvecklare — Kund Två AB", "2016 – 2020", "Byggde tjänster."])).Content;

        content.Experience.Count.ShouldBe(1);
        content.Sections.ShouldNotContain(s => s.Heading == "Arbetslivserfarenhet");
    }

    private static string Cv(bool blankBetweenEntries, string heading, params string[][] entries)
    {
        var lines = new List<string> { heading };
        foreach (var entry in entries)
        {
            if (blankBetweenEntries)
                lines.Add(string.Empty);
            lines.AddRange(entry);
        }

        return string.Join('\n', lines);
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
        Example tagline. Example coach for a fictional team.
        Example summary of a fictional career in software testing.
        Experience: See the attached portfolio for references.
        Example Role One Exampletown, Examplia Example Company One AB 01/2021 - Present
        I tested a fictional booking service.
        Example Role Two Exampleville, Examplia Example Company Two AB 01/2019 - 12/2020
        I ran tests for a fictional web shop during 2019-2020.
        • Automated tests for a fictional e-service, released 2019 - 2020 in two regions.
        Example Role Three Exampletown, Examplia Example Company Three AB 01/2017 - 12/2018
        Tested the 2018 release on two platforms.
        """;

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
        var cv = CompactEnglishCv.Replace("Experience: See the attached", "Experience only. See the attached", StringComparison.Ordinal);

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
