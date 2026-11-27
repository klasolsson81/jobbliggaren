using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Improvement;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #2342 — a bullet that the PDF layout wrapped onto two or more lines is ONE scoring unit, and
/// no criterion cites its tail as though it were a bullet of its own.
///
/// <para>Every CV here is synthetic (an invented person and invented employers) and has no blank
/// line, because PDF text never carries one. The English shape is the 2026-11-26 measurement:
/// a synthetic CV rendered by headless Chrome (11 pt Arial, A4) and read by the real extractor,
/// copied line for line. The Swedish CVs are authored to the same rule and assert their own
/// width premise.</para>
/// </summary>
public class WrappedPdfBulletTests
{
    // The measured document, one element per line: PDF text ends every line with a bare
    // newline, whatever line ends this source file is checked out with.
    internal static readonly string MeasuredEnglishCv = string.Join('\n',
    [
        "Testa Testsson",
        "testa.testsson@example.com | 070-000 00 00 | Exempelstad",
        "Profile",
        "Quality assurance engineer with a background in automated testing of web applications and a",
        "strong interest in continuous delivery and test design for distributed systems.",
        "Experience",
        "QA Engineer — Exempelbolaget AB",
        "2021 – 2024",
        "Automated 35 regression tests with Selenium and C#, reducing the regression cycle from two",
        "days to four hours.",
        "Designed a test strategy for the payment service together with three development teams and",
        "the product owner group.",
        "Introduced contract tests between the order service and the warehouse service, which removed",
        "a class of release blockers.",
        "Mentored two junior testers.",
        "Test Analyst — Fiktiva Konsult AB",
        "2018 – 2021",
        "Wrote and maintained manual test cases for an insurance portal used by customers of a",
        "regional insurance company.",
        "Reported defects in Jira and followed them up with the developers until they were verified and",
        "closed.",
        "Education",
        "BSc Computer Science — Exempeluniversitetet",
        "2015 – 2018",
        "Skills",
        "Selenium, C#, Playwright, SQL, Jira",
        "Languages",
        "Swedish, English",
    ]);

    internal static readonly string[] MeasuredEnglishUnits =
    [
        "Automated 35 regression tests with Selenium and C#, reducing the regression cycle from two\ndays to four hours.",
        "Designed a test strategy for the payment service together with three development teams and\nthe product owner group.",
        "Introduced contract tests between the order service and the warehouse service, which removed\na class of release blockers.",
        "Mentored two junior testers.",
        "Wrote and maintained manual test cases for an insurance portal used by customers of a\nregional insurance company.",
        "Reported defects in Jira and followed them up with the developers until they were verified and\nclosed.",
    ];

    private const string SwedishProfile =
        "Erfaren lagerarbetare med flera års erfarenhet av plock, packning och leveranser inom dagligvaruhandeln";

    // Ends mid-clause: any lower-case line after it reaches the measure.
    private const string FullSwedishLine =
        "Ledde planeringen av inventeringen på avdelningen tillsammans med två skiftledare och lagerchefen och";

    private static string SwedishCv(params string[] descriptionLines) =>
        WithTheSwedishMeasure(string.Join('\n',
        [
            "Testa Testsson",
            "testa.testsson@example.com",
            "Profil",
            SwedishProfile,
            "Erfarenhet",
            "Lagerarbetare — Exempellagret AB",
            "2018 – 2024",
            .. descriptionLines,
            "Utbildning",
            "Gymnasieexamen — Exempelgymnasiet",
            "2015 – 2018",
        ]));

    private static string WithTheSwedishMeasure(string cv)
    {
        var width = LineWrapMeasure.For(CvFileSignature.PdfContentType, cv).ShouldNotBeNull().Width;
        width.ShouldBe(SwedishProfile.Length, "premise: the profile line sets the measure");
        (FullSwedishLine.Length + 1 + 1).ShouldBeGreaterThanOrEqualTo(width,
            "premise: after the full line, any lower-case word reaches the measure");
        return cv;
    }

    internal static IReadOnlyList<string> Units(ParsedResume parsed)
    {
        var measure = LineWrapMeasure.For(parsed.SourceContentType, parsed.RawText);
        return [.. CvReviewContext.FromParsed(parsed).Content.Experience
            .SelectMany(e => ReviewText.DescriptionBullets(e, measure))];
    }

    private static IReadOnlyList<string> Units(string cv, string contentType = CvFileSignature.PdfContentType) =>
        Units(ResumeFromCvText(cv, sourceContentType: contentType));

    private static CvReviewEngine Engine() => new(
        RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
        AllCorrectSpellChecker(), RealAllowlist(),
        RealCvConventionsProvider(), RealParsingLexicon());

    internal static async Task<CvReviewResult> ReviewAsync(ParsedResume parsed) =>
        await Engine().ReviewAsync(CvReviewContext.FromParsed(parsed), RenderProfile.Ats,
            TestContext.Current.CancellationToken);

    internal static IEnumerable<TextSpan> QuotesOf(CvReviewResult result, string criterionId) =>
        Verdict(result, criterionId).Evidence.OfType<TextSpanEvidence>().Select(e => e.Span);

    // ── 1–2. A wrapped bullet is one unit ─────────────────────────────────

    [Fact]
    public void DescriptionBullets_ShouldJoinEachWrappedBullet_IntoOneUnitOfTheUsersOwnLines()
    {
        var parsed = ResumeFromCvText(MeasuredEnglishCv);

        var units = Units(parsed);

        units.ShouldBe(MeasuredEnglishUnits);
        foreach (var unit in units)
        {
            var span = ReviewText.Span(parsed.RawText, unit).Span;
            span.Start.ShouldBeGreaterThanOrEqualTo(0, $"the unit must resolve in RawText: {unit}");
            parsed.RawText.Substring(span.Start, span.Length).ShouldBe(span.Quote);
        }
    }

    [Fact]
    public void DescriptionBullets_ShouldJoinABulletWrappedOverThreeLines()
    {
        string[] lines =
        [
            "Ledde omläggningen av plockrutinerna på avdelningen tillsammans med två skiftledare och lagerchefen",
            "samt införde en ny kontrollrutin för utgående order som alla skift på lagret började använda under",
            "våren.",
        ];

        Units(SwedishCv(lines)).ShouldBe([string.Join('\n', lines)]);
    }

    // ── 3–6. Where the text admits another reading, the lines stay apart ──

    [Fact]
    public void DescriptionBullets_ShouldKeepAShortLowerCaseList_AsOneUnitPerItem()
    {
        string[] items = ["kassa", "kundservice", "varuplock och beställningar"];

        Units(SwedishCv(items)).ShouldBe(items);
    }

    // R1, accepted and known: a wrap before a capital, a digit or a glyph stays split.
    [Theory]
    [InlineData("Exempellagret AB:s ledning.")]
    [InlineData("40 personer i två skift.")]
    [InlineData("• Rapporterade avvikelser varje vecka.")]
    public void DescriptionBullets_ShouldNotJoin_ALineThatDoesNotOpenWithALowerCaseLetter(string next) =>
        Units(SwedishCv(FullSwedishLine, next)).ShouldBe([FullSwedishLine, next]);

    [Fact]
    public void DescriptionBullets_ShouldJoin_TheSameLowerCaseLineWithNothingBetween() =>
        Units(SwedishCv(FullSwedishLine, "var ansvarig för att alla avvikelser rapporterades."))
            .ShouldBe([$"{FullSwedishLine}\nvar ansvarig för att alla avvikelser rapporterades."]);

    [Fact]
    public void DescriptionBullets_ShouldNotJoinAcrossADateLine_ThatTheUnitSuppresses()
    {
        const string next = "var ansvarig för att alla avvikelser rapporterades.";

        // A lone month point, not a range: a range row under this line would anchor a second
        // entry (#2249), and the line would then never reach the unit at all.
        var parsed = ResumeFromCvText(SwedishCv(FullSwedishLine, "maj 2022", next));

        parsed.Content.Experience.ShouldHaveSingleItem().RawText.ShouldContain("\nmaj 2022\n",
            customMessage: "premise: the date line is inside the entry block");
        Units(parsed).ShouldBe([FullSwedishLine, next]);
    }

    [Fact]
    public void DescriptionBullets_ShouldNotJoinAcrossAnOrganisationLine_ThatTheUnitSuppresses()
    {
        // The "Title / Company / Dates" layout, with the employer repeated on a line of its own
        // between a line that reaches the measure and a lower-case line.
        const string next = "var ansvarig för att alla avvikelser rapporterades.";
        var parsed = ResumeFromCvText(WithTheSwedishMeasure(string.Join('\n',
        [
            "Testa Testsson", "Profil", SwedishProfile, "Erfarenhet",
            "Lagerarbetare", "Exempellagret AB", "2018 – 2024", FullSwedishLine, "Exempellagret AB", next,
        ])));

        parsed.Content.Experience.ShouldHaveSingleItem().Organization.ShouldBe("Exempellagret AB",
            "premise: the repeated line is the entry's organisation");
        Units(parsed).ShouldBe([FullSwedishLine, next]);
    }

    [Fact]
    public void DescriptionBullets_ShouldNeverJoinALineToTheHeader()
    {
        // The header is line 0 of the entry and is never part of a unit, even where the line
        // after it reads as its continuation. That tail stays a bullet of its own (R8, known).
        const string header =
            "Lagerarbetare — Exempellagret AB i Exempelstad med ansvar för inventering, beställningar och planering av";
        const string tail = "leveranser till butikerna i regionen.";
        var parsed = ResumeFromCvText(string.Join('\n',
            ["Testa Testsson", "Profil", SwedishProfile, "Erfarenhet", header, tail, "2018 – 2024"]));

        var entry = parsed.Content.Experience.ShouldHaveSingleItem();
        entry.RawText.ShouldStartWith(header, customMessage: "premise: the long line is the entry's header");
        entry.Organization.ShouldNotBe(tail, "premise: the tail is not read as the organisation");
        LineWrapMeasure.For(parsed.SourceContentType, parsed.RawText).ShouldNotBeNull().Continues(header, tail)
            .ShouldBeTrue("premise: the tail reads as the wrap of the header line");
        Units(parsed).ShouldBe([tail]);
    }

    // ── 8–9. Where no line end is a layout wrap, nothing changes ───────────

    [Theory]
    [InlineData(CvFileSignature.DocxContentType)]
    [InlineData("application/octet-stream")]
    public void DescriptionBullets_ShouldKeepOneUnitPerLine_WhenTheFileIsNotDeclaredAsPdf(string contentType)
    {
        var units = Units(MeasuredEnglishCv, contentType);

        units.ShouldBe(MeasuredEnglishUnits.SelectMany(u => u.Split('\n')).ToList());
    }

    // A canonical description is typed by the user, so each line end is authored.
    private const string CanonicalDescription =
        "Automated 35 regression tests with Selenium and C#, reducing the regression cycle from two\ndays to four hours.";

    private static CvReviewContext CanonicalContext()
    {
        var content = new ResumeContent(
            new PersonalInfo("Testa Testsson", "testa.testsson@example.com", null, "Exempelstad"),
            experiences:
            [
                new Experience("Exempelbolaget AB", "QA Engineer",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 1, 1), CanonicalDescription),
            ]);
        return CvReviewContext.FromCanonical(content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.En);
    }

    [Fact]
    public void DescriptionBullets_ShouldKeepEveryAuthoredLine_OnTheCanonicalArm()
    {
        var context = CanonicalContext();

        context.SourceContentType.ShouldBeNull("premise: a canonical CV has no source file");
        var measure = LineWrapMeasure.For(context.SourceContentType, context.LinearText);
        measure.ShouldBeNull();
        ReviewText.DescriptionBullets(context.Content.Experience.ShouldHaveSingleItem(), measure)
            .ShouldBe(CanonicalDescription.Split('\n'));
    }

    [Fact]
    public void DescriptionBullets_OnTheCanonicalArm_IgnoresAMeasureThatNoCallerCanPassIt()
    {
        var context = CanonicalContext();
        var measure = LineWrapMeasure.For(CvFileSignature.PdfContentType, context.LinearText).ShouldNotBeNull();

        ReviewText.DescriptionBullets(context.Content.Experience.ShouldHaveSingleItem(), measure)
            .ShouldBe(CanonicalDescription.Split('\n'));
    }

    // ── 13. Review and improve read the same unit ─────────────────────────

    [Fact]
    public void DescriptionBullets_ShouldYieldTheSameUnits_FromTheParsedAndTheReviewableEntry()
    {
        var parsed = ResumeFromCvText(MeasuredEnglishCv);
        var measure = LineWrapMeasure.For(parsed.SourceContentType, parsed.RawText);

        var fromParsed = parsed.Content.Experience.SelectMany(e => ReviewText.DescriptionBullets(e, measure));

        fromParsed.ShouldBe(Units(parsed));
    }

    // ── 12. A1/A2/A6 cite a whole bullet, never its tail ──────────────────

    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("A6")]
    public async Task Criterion_ShouldCiteAWholeBullet_NeverTheTailOfAWrappedOne(string criterionId)
    {
        var parsed = ResumeFromCvText(MeasuredEnglishCv);

        var result = await ReviewAsync(parsed);

        var quotes = QuotesOf(result, criterionId).ToList();
        quotes.ShouldNotBeEmpty($"premise: {criterionId} cites a text span on this CV");
        foreach (var span in quotes)
        {
            MeasuredEnglishUnits.ShouldContain(span.Quote, $"{criterionId} must cite a whole bullet");
            span.Quote.ShouldNotBe("days to four hours.");
            parsed.RawText.Substring(span.Start, span.Length).ShouldBe(span.Quote);
        }
    }

    private const string LoneCapitalLine =
        "Utbildade nya kollegor i säker körning av motviktstruckar och skjutstativtruckar enligt truckklass A";

    private const string NamedSystemLine =
        "Förde in varje inleverans och varje avvikelse i lagret tillsammans med två inköpare i Exempelsystemet";

    [Theory]
    [InlineData(LoneCapitalLine, CriterionVerdict.Fail)]
    [InlineData(NamedSystemLine, CriterionVerdict.Pass)]
    public async Task A6_ShouldReadTheWordsOfAWrappedBullet_AsItsLinesReadThem(
        string firstLine, CriterionVerdict expected)
    {
        const string continuation = "och tog över utbildningen av nya kollegor.";
        var parsed = ResumeFromCvText(SwedishCv(firstLine, continuation));
        var bullet = $"{firstLine}\n{continuation}";
        Units(parsed).ShouldBe([bullet], "premise: the two lines are one wrapped bullet");

        var result = await ReviewAsync(parsed);

        Verdict(result, "A6").Verdict.ShouldBe(expected);
        QuotesOf(result, "A6").Select(s => s.Quote).ShouldBe([bullet]);
    }

    // ── 10. B5 sees the same lead glyphs ──────────────────────────────────

    [Fact]
    public async Task B5_ShouldStillSeeEveryLeadGlyph_WhenGlyphLedBulletsWrap()
    {
        // Two glyph styles, each on a wrapped bullet: B5 must still see both, since a
        // continuation can never open with a glyph.
        var parsed = ResumeFromCvText(SwedishCv(
            $"• {FullSwedishLine}", "var ansvarig för att alla avvikelser rapporterades.",
            $"- {FullSwedishLine}", "rapporterade avvikelser varje vecka."));

        Units(parsed).Count.ShouldBe(2, "premise: each glyph-led bullet and its wrap is one unit");

        var result = await ReviewAsync(parsed);

        Verdict(result, "B5").Verdict.ShouldBe(CriterionVerdict.Warn);
    }

    // ── 11. The improve side reads the joined unit too ────────────────────

    private static async Task<IReadOnlyList<ProposedChange>> WeakVerbProposalsAsync(ParsedResume parsed)
    {
        var engine = new CvImprovementEngine(RealClicheLexicon(), RealVerbMapper(), RealRubricProvider(),
            Analyzer(), RealCvConventionsProvider(), RealParsingLexicon());
        var result = await engine.SuggestAsync(parsed, null, RenderProfile.Ats,
            TestContext.Current.CancellationToken);
        return [.. result.Changes.Where(c => c.Kind == ProposedChangeKind.WeakVerbUpgrade)];
    }

    [Fact]
    public async Task WeakVerbTransform_ShouldNotProposeARewrite_ForAWeakVerbInTheMiddleOfAWrappedBullet()
    {
        var parsed = ResumeFromCvText(SwedishCv(FullSwedishLine, "var ansvarig för att alla avvikelser rapporterades."));

        (await WeakVerbProposalsAsync(parsed)).ShouldBeEmpty();
    }

    [Fact]
    public async Task WeakVerbTransform_ShouldProposeOneRewrite_ForTheVerbatimOpenerOfAWrappedBullet()
    {
        var parsed = ResumeFromCvText(SwedishCv(
            "Var ansvarig för planeringen av inventeringen på avdelningen tillsammans med två skiftledare och",
            "lagerchefen under hela året."));

        var change = (await WeakVerbProposalsAsync(parsed)).ShouldHaveSingleItem();

        change.Replacement!.Before.ShouldBe("Var ansvarig för");
    }
}
