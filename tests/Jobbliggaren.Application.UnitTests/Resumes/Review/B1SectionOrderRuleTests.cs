using System.IO;
using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
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
    public async Task B1_ShouldWarn_WhenTheOutOfOrderSectionIsWrittenInline()
    {
        // BLOCKER 2, at the criterion level. "Kompetenser: C#, SQL" IS a section to the segmenter
        // (#421). An order analyzer that only matched WHOLE lines could not see it — and this CV,
        // which really does put Kompetenser first, was reported as correctly ordered. The analyzer
        // now runs the segmenter's own detector, so the two cannot disagree about what a heading is.
        var b1 = await B1Async(Resume(
            rawText: "Kompetenser: C#, SQL\n\nArbetslivserfarenhet\nDev 2021–2024\n\nUtbildning\nKTH"));

        b1.Verdict.ShouldBe(CriterionVerdict.Warn,
            "En inline-skriven sektion är en sektion — den får inte vara osynlig för ordningen.");
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

    [Theory]
    [InlineData("En text helt utan igenkännbara rubriker")]
    [InlineData("Arbetslivserfarenhet\nBackend-utvecklare 2021–2024")]
    public async Task B1_ShouldNotClaimTheOrderIsRecommended_WhenFewerThanTwoSectionsWereRecognised(
        string rawText)
    {
        // THE MIS-REPORT THIS STEP'S OWN FIX COMMITTED, caught by both review gates. A CV whose raw
        // text carries fewer than two recognisable headings (a one-column layout the extractor
        // flattened, say) has an order nobody looked at. `Deviates == false` is true — and it means
        // "we saw nothing", NOT "it is correct".
        //
        // The VERDICT is Pass and that is right: presence is judged from the parsed content, which
        // is intact, and NotAssessed would withdraw a High-weight criterion while claiming we could
        // not read something we read perfectly well. But the CLAIM must narrow to what was observed
        // — "sektionerna står i rekommenderad ordning" would be a green light on a dimension never
        // inspected, which is precisely the defect 8b.4b exists to remove.
        var b1 = await B1Async(Resume(rawText: rawText));

        b1.Verdict.ShouldBe(CriterionVerdict.Pass, "presence är bedömd och intakt.");

        b1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldNotContain("står i rekommenderad ordning", Case.Sensitive,
                "B1 får inte påstå ordnings-efterlevnad för ett CV vars rubriker den aldrig läste.");
    }

    [Fact]
    public async Task B1_ShouldSayTheOrderCouldNotBeRead_WhenNoHeadingsAreRecognised()
    {
        // The positive half of the test above: the evidence must not merely OMIT the claim, it must
        // say WHY — an honest ceiling stated out loud (§5), not a silence the user has to interpret.
        var b1 = await B1Async(Resume(rawText: "En text helt utan igenkännbara rubriker"));

        b1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldContain("gick inte att läsa");
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

    // ===============================================================
    // (e) #890 — the FAIL arm: "kreativ ordning som döljer kärninfo"
    // ===============================================================
    //
    // The definition, CTO-bound 2026-07-25: a core section (kontakt/erfarenhet/utbildning) is HIDDEN
    // when the sections preceding it are ones the convention ranks AFTER it; the measure is the count
    // of those displacing sections, and the rubric owns the count at which Warn becomes Fail (3).
    //
    // The layout table below IS the definition's justification, and it is a permanent regression
    // asset rather than a one-off argument: every "good CV" row must stay out of Fail, and the two
    // chaotic rows must reach it. A definition that fails a good CV is the failure mode the previous
    // CTO bind deferred this issue over, so the boundary row (displacement 2, Warn) is the load-
    // bearing one — see B1_ShouldFailTheBoundaryCv_WhenTheThresholdIsLoweredToTwo for the proof.

    private const string StudentOrder =
        "Kontakt\nanna@example.se\nUtbildning\nKTH, 2016–2021\n"
        + "Arbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\nKompetenser\nC#, SQL";

    private const string CompetenceFirst =
        "Kontakt\nanna@example.se\nProfil\nErfaren utvecklare.\nKompetenser\nC#, SQL\n"
        + "Arbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\nUtbildning\nKTH, 2016–2021";

    // THE BOUNDARY: two sections the convention ranks after experience sit before it. Displacement 2
    // → Warn. This CV is fine, and the threshold exists to keep it that way.
    private const string CompetenceAndLanguageFirst =
        "Kontakt\nanna@example.se\nProfil\nErfaren utvecklare.\nKompetenser\nC#, SQL\n"
        + "Språk\nSvenska, engelska\nArbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021";

    // Contact LAST. Kontakt is rank 0, so EVERY section before it displaces it: displacement 6 → Fail.
    // (The CTO report's table said 4 for this layout; counted against the shipped convention it is 6.
    // The verdict is unchanged — both are far past the threshold — but the number in the evidence is
    // the one the user reads, so it is asserted, not assumed.)
    private const string ContactBuried =
        "Profil\nErfaren utvecklare.\nKompetenser\nC#, SQL\nSpråk\nSvenska, engelska\n"
        + "Intressen\nLöpning\nArbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021\nKontakt\nanna@example.se";

    // The chaotic CV: everything before contact, experience below education. Displacement 6 → Fail.
    // Same count as ContactBuried by coincidence of length; the layouts differ in WHICH sections bury.
    private const string FullyScrambled =
        "Intressen\nLöpning\nReferenser\nPå begäran\nKompetenser\nC#, SQL\nSpråk\nSvenska, engelska\n"
        + "Utbildning\nKTH, 2016–2021\nArbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\n"
        + "Kontakt\nanna@example.se";

    [Theory]
    [InlineData(InConventionOrder)]
    [InlineData(StudentOrder)]
    [InlineData(CompetenceFirst)]
    [InlineData(CompetenceAndLanguageFirst)]
    public async Task B1_ShouldNotFail_OnAGoodCv(string rawText)
    {
        // The education-first student CV and the competence-first CV are ordinary, defensible Swedish
        // layouts — Arbetsförmedlingen's own guidance describes the latter as acceptable. Failing them
        // would be the §5 over-claim delivered on a document that is fine, which is precisely why the
        // earlier bind refused to ship a Fail arm without a measured definition.
        var b1 = await B1Async(Resume(rawText: rawText));

        b1.Verdict.ShouldNotBe(CriterionVerdict.Fail);
    }

    [Theory]
    [InlineData(ContactBuried)]
    [InlineData(FullyScrambled)]
    public async Task B1_ShouldFail_WhenACoreSectionIsBuried(string rawText)
    {
        var b1 = await B1Async(Resume(rawText: rawText));

        b1.Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    [Fact]
    public async Task B1_ShouldCiteTheBuriedSectionAndItsDisplacers_WhenItFails()
    {
        // A Fail the user cannot check against her own document is an opaque judgement (§5). The
        // evidence names the buried section, HOW MANY sections bury it and WHICH — all in her own
        // headings — and deliberately says nothing about ATS behaviour or "döljer": we observe a
        // section order, we do not observe an ATS, and "döljer kärninfo" is the criterion's label,
        // not a claim about her document.
        var b1 = await B1Async(Resume(rawText: ContactBuried));

        var observation = b1.Evidence.ShouldHaveSingleItem()
            .ShouldBeOfType<StructuralEvidence>().Observation;

        observation.ShouldContain("Kontakt");
        observation.ShouldContain("6 sektioner");
        observation.ShouldContain("Intressen");
        observation.ShouldContain("Nuvarande ordning:");
        observation.ShouldContain("Rekommenderad ordning:");
        observation.ShouldNotContain("döljer");
        observation.ShouldNotContain("ATS");
    }

    [Fact]
    public async Task B1_ShouldFailTheBoundaryCv_WhenTheThresholdIsLoweredToTwo()
    {
        // THE PROOF THE ISSUE DEMANDS: "en definition som fäller ett bra CV måste gå röd".
        //
        // Driven through the REAL RubricLoader over a synthetic asset whose only difference is the
        // threshold, so this is the shipped rule reading different data — not a reimplementation.
        // At 2, the competence-and-language-first CV (displacement 2) Fails. That CV is fine, which
        // is exactly why the shipped threshold is 3, and this test is the permanent record of the
        // choice rather than a sentence in a PR body that no one can re-run.
        var lowered = LoweredDisplacementThresholdRubric(2);

        var engine = new CvReviewEngine(
            lowered, RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

        var b1 = Verdict(
            await engine.ReviewAsync(
                CvReviewContext.FromParsed(Resume(rawText: CompetenceAndLanguageFirst)),
                RenderProfile.Ats, TestContext.Current.CancellationToken),
            "B1");

        b1.Verdict.ShouldBe(CriterionVerdict.Fail,
            "vid tröskel 2 fälls ett bra CV — det är därför den skeppade tröskeln är 3.");

        // And the same CV at the SHIPPED threshold must NOT fail, or the assertion above would prove
        // nothing about the threshold (it would pass for any rule that always fails this CV).
        (await B1Async(Resume(rawText: CompetenceAndLanguageFirst))).Verdict
            .ShouldNotBe(CriterionVerdict.Fail);
    }

    /// <summary>The shipped rubric with ONLY B1's displacement threshold rewritten, loaded through the
    /// real <c>RubricLoader.LoadFrom</c> seam. The mutation is asserted to have landed — an unmatched
    /// replace is a silent no-op, and the test would then pass for the wrong reason.</summary>
    private static StubRubricProvider LoweredDisplacementThresholdRubric(int failAtLeast)
    {
        const string shipped = "\"coreSectionDisplacementFailAtLeast\": 3";
        var json = ShippedRubricJson();

        json.Contains(shipped, StringComparison.Ordinal).ShouldBeTrue(
            "test-buggen: tröskeln finns inte i det skeppade assetet — mutationen landade aldrig.");

        var mutated = json.Replace(
            shipped, $"\"coreSectionDisplacementFailAtLeast\": {failAtLeast}", StringComparison.Ordinal);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(mutated));
        return new StubRubricProvider(RubricLoader.LoadFrom(stream));
    }

    private static string ShippedRubricJson()
    {
        var assembly = typeof(RubricProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Jobbliggaren.Infrastructure.KnowledgeBank.rubric.v2.3.0.json")
            ?? throw new InvalidOperationException("Det inbäddade rubrik-assetet saknas.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class StubRubricProvider(Rubric rubric) : IRubricProvider
    {
        public Rubric GetRubric() => rubric;
    }
}
