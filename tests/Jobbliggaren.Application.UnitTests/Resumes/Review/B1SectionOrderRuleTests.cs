using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b 8b.4b (ADR 0108) — B1 "Sektioner och ordning" finally assesses BOTH halves of its own
/// name.
///
/// <para><b>The defect these tests close.</b> The rubric's B1 <c>atsPassSignal</c> has carried the
/// order chain since v2.1.0 and <b>no code had ever read it</b>. The rule checked PRESENCE only and
/// returned <c>Pass</c> — so a CV with a chaotic section order was handed a green
/// "Sektioner och ordning · Godkänt" on the very dimension the criterion's <c>atsFailSignal</c>
/// calls out ("kreativ ordning som döljer kärninfo"). B1 also had <b>no tests at all</b>, which is
/// how the mis-report survived: nothing described what it was supposed to say.</para>
///
/// <para><b>NotAssessed is deliberately unreachable</b> and there is a test for that. It was the
/// first fix attempted and it is its own mis-report: every authored <c>notAssessedReason</c> means
/// "we could not read this from a text-based interpretation of your CV", which is FALSE for a CV
/// whose sections we read perfectly well — and the engine counts <c>Verdict != NotAssessed</c> as
/// the assessed set, so every well-formed CV would silently lose a High-weight criterion.</para>
/// </summary>
public class B1SectionOrderRuleTests
{
    private static CvReviewEngine NewEngine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> B1Async(ParsedResume resume) =>
        Verdict(
            await NewEngine().ReviewAsync(
                CvReviewContext.FromParsed(resume), RenderProfile.Ats,
                TestContext.Current.CancellationToken),
            "B1");

    // The convention (cv-conventions.v1.json): contact → profile → experience → education → skills
    // → languages. These two raw texts hold the SAME sections; only the order differs.
    private const string InConventionOrder =
        "Kontakt\nanna@example.se\nArbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021";

    private const string OutOfConventionOrder =
        "Kontakt\nanna@example.se\nUtbildning\nKTH, 2016–2021\n"
        + "Arbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024";

    // ===============================================================
    // (a) The ORDER half — the dimension that never existed
    // ===============================================================

    [Fact]
    public async Task B1_ShouldWarn_WhenTheCoreSectionsArePresentButOutOfOrder()
    {
        // THE DEFECT, INVERTED. Before 8b.4b this CV was scored Pass.
        var b1 = await B1Async(Resume(rawText: OutOfConventionOrder));

        b1.Verdict.ShouldBe(CriterionVerdict.Warn,
            "utbildning före arbetslivserfarenhet avviker från konventionen — B1 får inte ge grönt.");
    }

    [Fact]
    public async Task B1_ShouldCiteBothOrders_WhenItWarnsOnTheOrder()
    {
        // A verdict the user cannot act on is an opaque judgement (§5). The evidence names the
        // observed and the recommended order, in her OWN headings.
        var b1 = await B1Async(Resume(rawText: OutOfConventionOrder));

        var observation = b1.Evidence.ShouldHaveSingleItem()
            .ShouldBeOfType<StructuralEvidence>().Observation;

        observation.ShouldContain("Nuvarande ordning: Kontakt, Utbildning, Arbetslivserfarenhet");
        observation.ShouldContain("Rekommenderad ordning: Kontakt, Arbetslivserfarenhet, Utbildning");
    }

    [Fact]
    public async Task B1_ShouldPass_WhenTheCoreSectionsArePresentAndInTheRecommendedOrder()
    {
        // The mirror of the Warn above. Without this, "Warn on deviation" would be
        // indistinguishable from "never Pass again" — which would punish every well-formed CV.
        var b1 = await B1Async(Resume(rawText: InConventionOrder));

        b1.Verdict.ShouldBe(CriterionVerdict.Pass);
        b1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldContain("rekommenderad ordning");
    }

    // ===============================================================
    // (b) The PRESENCE half — unchanged, but pinned for the first time
    // ===============================================================

    [Fact]
    public async Task B1_ShouldFail_WhenACoreSectionIsMissing_RegardlessOfOrder()
    {
        // The rubric's lead fail signal ("Saknar erfarenhet/utbildning") outranks the order — a CV
        // with no education has a bigger problem than the sequence of the sections it does have.
        var b1 = await B1Async(Resume(education: [], rawText: OutOfConventionOrder));

        b1.Verdict.ShouldBe(CriterionVerdict.Fail);
        b1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldContain("utbildning");
    }

    // ===============================================================
    // (c) NotAssessed is UNREACHABLE — the fix that was rejected
    // ===============================================================

    [Theory]
    [InlineData(InConventionOrder)]
    [InlineData(OutOfConventionOrder)]
    [InlineData("")]
    [InlineData("En text helt utan igenkännbara rubriker")]
    public async Task B1_ShouldNeverReportNotAssessed_BecauseACriterionThatCanAssessMustAssess(string rawText)
    {
        // NotAssessed withdraws the criterion from the assessed set (CvReviewEngine: assessedCount
        // counts Verdict != NotAssessed), and its authored reason would claim we could not read
        // something we read perfectly well. B1 must always land on a real verdict.
        var b1 = await B1Async(Resume(rawText: rawText));

        b1.Verdict.ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    [Fact]
    public async Task B1_ShouldPass_WhenNoHeadingsAreRecognised_BecauseZeroSectionsCannotBeOutOfOrder()
    {
        // A CV whose raw text carries no recognisable headings (a one-column layout the extractor
        // flattened, say) yields fewer than two observed sections. That is NOT evidence of a wrong
        // order — claiming it were would be a fabricated verdict. Presence is judged from the
        // parsed content, which is intact here, so B1 passes on the half it can see.
        var b1 = await B1Async(Resume(rawText: "En text helt utan igenkännbara rubriker"));

        b1.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    // ===============================================================
    // (d) The CANONICAL arm — Pass by construction (the D1 idiom)
    // ===============================================================

    [Fact]
    public async Task B1_ShouldPass_OnTheCanonicalArm_BecauseTheLinearizerEmitsCanonicalOrder()
    {
        // App-managed content is emitted by the linearizer in canonical order BY CONSTRUCTION
        // (ADR 0097 §2). The answer is known — hiding it behind a hedge would misreport, exactly as
        // D1FileFormatRule argues for its own canonical arm.
        var content = new ResumeContent(
            new PersonalInfo("Anna Andersson", "anna@example.se", "070-123 45 67", "Stockholm"),
            experiences:
            [
                new Experience("Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 1, 1),
                    "Levererade 3 plattformsmigrationer."),
            ],
            educations:
            [
                new Education("KTH", "Civilingenjör", new DateOnly(2016, 8, 1), new DateOnly(2021, 6, 1)),
            ]);

        var result = await NewEngine().ReviewAsync(
            CvReviewContext.FromCanonical(content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        Verdict(result, "B1").Verdict.ShouldBe(CriterionVerdict.Pass);
    }
}
