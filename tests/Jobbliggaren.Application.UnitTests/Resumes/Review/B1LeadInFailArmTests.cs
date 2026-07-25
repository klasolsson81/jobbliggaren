using System.IO;
using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #890 — B1's FAIL arm: a long LEAD-IN before the first core section.
///
/// <para><b>The definition</b> (senior-cto-advisor re-bind 2026-07-25): how many sections stand before
/// the FIRST core section (kontakt/erfarenhet/utbildning), measured only when EVERY core section was
/// observed, Fail at 4.</para>
///
/// <para><b>Why this file exists as its own suite.</b> The first attempt counted, per core section,
/// the sections above it that the convention ranks AFTER it — and it FAILED ordinary Swedish CVs,
/// because a section the convention does not name gets "ranks last" by construction. The healthcare CV
/// that leads with Legitimation, the IT CV with Certifieringar and Kurser, the portfolio CV with
/// several project sections, and a CV built from two layouts the bind itself called acceptable all
/// came out Fail — the last of them telling the user her own Utbildning section was burying her
/// experience.</para>
///
/// <para><b>The matrix is written so that class of error cannot return unnoticed.</b> Every
/// "must not Fail" row carries free sections, deliberately: with only three RANKED non-core sections
/// in the convention, a lead-in of 4 ENTAILS a free section early. That is a theorem of the
/// convention, not an artifact of these fixtures — so a suite whose good CVs have no free sections
/// cannot distinguish the intended rule from "has a free section early", which is exactly how the
/// first version passed.</para>
/// </summary>
public class B1LeadInFailArmTests
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

    private const string RecommendedOrder =
        "Kontakt\nanna@example.se\nArbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021";

    // ── Ordinary Swedish layouts that MUST NOT Fail. Each one Failed under the first definition. ──

    private const string HealthcareCv =
        "Kontakt\nanna@example.se\nProfil\nErfaren undersköterska.\nLegitimation\nSocialstyrelsen 2015\n"
        + "Kompetenser\nOmvårdnad, journalföring\nKörkort\nB\n"
        + "Arbetslivserfarenhet\nUndersköterska, Vårdcentralen, 2015–2024\n"
        + "Utbildning\nOmvårdnadsprogrammet, 2012–2015";

    private const string ItCv =
        "Kontakt\nanna@example.se\nProfil\nBackend-utvecklare.\nKompetenser\nC#, SQL\n"
        + "Certifieringar\nAZ-204\nKurser\nDistribuerade system\n"
        + "Arbetslivserfarenhet\nUtvecklare, Acme AB, 2021–2024\nUtbildning\nKTH, 2016–2021";

    private const string PortfolioCv =
        "Kontakt\nanna@example.se\nProfil\nFormgivare.\nProjekt\nIdentitet för Acme\n"
        + "Egna projekt\nTypsnittet Nord\nUtvalda projekt\nUtställning 2023\n"
        + "Arbetslivserfarenhet\nFormgivare, Studio, 2019–2024\nUtbildning\nKonstfack, 2015–2019";

    /// <summary>Education-first (bind row 2, "must not Fail") composed with
    /// competence-and-language-first (bind row 4, "the boundary") — the composition that broke the
    /// first definition.</summary>
    private const string StudentCompetenceComposition =
        "Kontakt\nanna@example.se\nProfil\nNyexaminerad.\nKompetenser\nC#, SQL\nSpråk\nSvenska, engelska\n"
        + "Utbildning\nKTH, 2016–2021\nArbetslivserfarenhet\nPraktikant, Acme AB, 2021–2022";

    /// <summary>The two-column sidebar CV: linearised, Profil/Kompetenser/Språk land ahead of Kontakt.
    /// Lead-in 3 — the LONGEST lead-in that must still not Fail, i.e. the calibration boundary the
    /// shipped threshold of 4 sits one step above.</summary>
    private const string SidebarCv =
        "Profil\nErfaren utvecklare.\nKompetenser\nC#, SQL\nSpråk\nSvenska, engelska\n"
        + "Kontakt\nanna@example.se\nArbetslivserfarenhet\nUtvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021";

    // ── The counterfactual pair: same seven sections, one heading moved. ──────────────────

    private const string ContactFirst =
        "Kontakt\nanna@example.se\nProfil\nErfaren utvecklare.\nKompetenser\nC#, SQL\nSpråk\nSvenska\n"
        + "Intressen\nLöpning\nArbetslivserfarenhet\nUtvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021";

    private const string ContactLast =
        "Profil\nErfaren utvecklare.\nKompetenser\nC#, SQL\nSpråk\nSvenska\nIntressen\nLöpning\n"
        + "Arbetslivserfarenhet\nUtvecklare, Acme AB, 2021–2024\nUtbildning\nKTH, 2016–2021\n"
        + "Kontakt\nanna@example.se";

    /// <summary>The same buried layout with the Kontakt HEADING removed — the contact block is
    /// unheaded, which is the common Swedish top-of-page form. The core set is then incomplete and the
    /// measure must refuse.</summary>
    private const string ContactLastUnheaded =
        "Anna Andersson\nanna@example.se\nProfil\nErfaren utvecklare.\nKompetenser\nC#, SQL\n"
        + "Språk\nSvenska\nIntressen\nLöpning\nArbetslivserfarenhet\nUtvecklare, Acme AB, 2021–2024\n"
        + "Utbildning\nKTH, 2016–2021";

    [Theory]
    [InlineData(RecommendedOrder, CriterionVerdict.Pass)]
    [InlineData(HealthcareCv, CriterionVerdict.Warn)]
    [InlineData(ItCv, CriterionVerdict.Warn)]
    [InlineData(PortfolioCv, CriterionVerdict.Warn)]
    [InlineData(StudentCompetenceComposition, CriterionVerdict.Warn)]
    [InlineData(SidebarCv, CriterionVerdict.Warn)]
    [InlineData(ContactFirst, CriterionVerdict.Warn)]
    public async Task B1_ShouldNotFail_OnAnOrdinaryCv(string rawText, CriterionVerdict expected)
    {
        // The EXACT verdict, not merely "not Fail": ShouldNotBe(Fail) also passes for NotAssessed and
        // for a rule that has silently degenerated into always-Pass or always-Warn.
        (await B1Async(Resume(rawText: rawText))).Verdict.ShouldBe(expected);
    }

    [Fact]
    public async Task B1_ShouldSeparatePositionFromPresence_OnTheSameSevenSections()
    {
        // THE COUNTERFACTUAL, as one test so it cannot be half-deleted. Same sections, one heading
        // moved. The first definition Failed BOTH of these, which is what proved it was measuring
        // presence — how many sections the CV has — rather than position.
        (await B1Async(Resume(rawText: ContactFirst))).Verdict.ShouldBe(CriterionVerdict.Warn);
        (await B1Async(Resume(rawText: ContactLast))).Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    [Fact]
    public async Task B1_ShouldNotFail_WhenACoreSectionHasNoHeading_BecauseTheMeasureRefuses()
    {
        // The validity precondition, end to end. With the Kontakt heading gone the core set is
        // incomplete, so a lead-in would count sections that may well sit BELOW an unlocated core
        // section: it can only over-count, and the bias is unbounded on a rich CV. Refusing is the
        // honest answer, and the criterion still Warns on the deviation it CAN see.
        // The exact verdict, not "not Fail": the CV still deviates, so Warn is the answer, and a rule
        // that silently stopped assessing (NotAssessed) would pass a ShouldNotBe.
        (await B1Async(Resume(rawText: ContactLastUnheaded))).Verdict.ShouldBe(CriterionVerdict.Warn);
    }

    [Fact]
    public async Task B1_ShouldCiteTheFirstCoreSectionAndWhatPrecedesIt_WhenItFails()
    {
        // A Fail the user cannot check against her own document is an opaque judgement (§5). The
        // evidence names the first core section, HOW MANY sections stand ahead of it and WHICH — all
        // in her own headings — and deliberately says nothing about ATS behaviour or "döljer".
        var b1 = await B1Async(Resume(rawText: ContactLast));

        var observation = b1.Evidence.ShouldHaveSingleItem()
            .ShouldBeOfType<StructuralEvidence>().Observation;

        // The preceding list is asserted EXACTLY, not by fragment. The full observed order contains
        // every one of these words too, so a ShouldContain on a name could not tell the two apart —
        // and citing the observed order instead of the preceding sections is precisely the mutation
        // that survived the first version of this suite.
        observation.ShouldContain(
            "Den första kärnsektionen \"Arbetslivserfarenhet\" står efter 4 andra sektioner: "
            + "Profil, Kompetenser, Språk, Intressen.");
        observation.ShouldContain("Nuvarande ordning:");
        observation.ShouldContain("Rekommenderad ordning:");
        observation.ShouldNotContain("döljer");
        observation.ShouldNotContain("ATS");
    }

    [Fact]
    public async Task B1_ShouldFailTheSidebarCv_WhenTheThresholdIsLoweredToThree()
    {
        // THE PROOF THE ISSUE DEMANDS: "en definition som fäller ett bra CV måste gå röd".
        //
        // Driven through the REAL RubricLoader over a synthetic asset whose only difference is the
        // threshold, so this is the shipped rule reading different data, not a reimplementation. At 3
        // the two-column sidebar CV Fails. That CV is fine, which is exactly why the shipped threshold
        // is 4 — and this test is the permanent record of the choice rather than a sentence in a PR
        // body nobody can re-run.
        var engine = new CvReviewEngine(
            LoweredLeadInThresholdRubric(3), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

        var lowered = Verdict(
            await engine.ReviewAsync(
                CvReviewContext.FromParsed(Resume(rawText: SidebarCv)),
                RenderProfile.Ats, TestContext.Current.CancellationToken),
            "B1");

        lowered.Verdict.ShouldBe(CriterionVerdict.Fail,
            "vid tröskel 3 fälls sidebar-CV:t — det är därför den skeppade tröskeln är 4.");

        // And the same CV at the SHIPPED threshold must NOT fail, or the assertion above would prove
        // nothing about the threshold: it would pass for any rule that always fails this CV. Together
        // the two pin the shipped number to exactly 4.
        (await B1Async(Resume(rawText: SidebarCv))).Verdict.ShouldNotBe(CriterionVerdict.Fail);
    }

    [Fact]
    public async Task B1_ShouldWarnAndNameKontakt_WhenOnlyTheContactSectionIsMissing()
    {
        // B1's PRESENCE half, pinned for the first time — the OTHER holder of the core-section set.
        // The asset-side pin (CvConventionsProviderTests) can only catch the asset drifting; without
        // this one the rule side could be renamed freely. Mutation-verified: changing this label used
        // to leave all 17806 tests green.
        var b1 = await B1Async(Resume(
            contact: new ParsedContact(null, null, null, null), rawText: RecommendedOrder));

        b1.Verdict.ShouldBe(CriterionVerdict.Warn);

        // The EXACT sentence, not a ShouldContain("kontakt"): a mutated label ("kontaktuppgifter-MUT")
        // still contains "kontakt" as a substring, so the loose assertion passed the mutation it was
        // written to catch. Measured, not assumed — the first version of this very test did exactly
        // that.
        b1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>()
            .Observation.ShouldBe("Saknar sektion(er): kontakt.");
    }

    [Fact]
    public async Task B1_ShouldNotFail_WhenTheDeeplyBuriedSectionIsNotCore()
    {
        // Profil sits behind five sections — and Profil is RECOMMENDED, not core. Failing here would
        // be exactly the over-claim the conservative coreSections list exists to prevent, and without
        // this test the rule could ignore coreSections entirely and stay green.
        var b1 = await B1Async(Resume(rawText:
            "Kontakt\nanna@example.se\nArbetslivserfarenhet\nUtvecklare, Acme AB, 2021–2024\n"
            + "Utbildning\nKTH, 2016–2021\nKompetenser\nC#\nSpråk\nSvenska\nIntressen\nLöpning\n"
            + "Profil\nErfaren utvecklare."));

        b1.Verdict.ShouldBe(CriterionVerdict.Warn);
    }

    /// <summary>The shipped rubric with ONLY B1's lead-in threshold rewritten, loaded through the real
    /// <c>RubricLoader.LoadFrom</c> seam. The mutation is asserted to have landed — an unmatched
    /// replace is a silent no-op, and the test would then pass for the wrong reason.</summary>
    private static StubRubricProvider LoweredLeadInThresholdRubric(int failAtLeast)
    {
        const string shipped = "\"coreLeadInFailAtLeast\": 4";
        var json = ShippedRubricJson();

        json.Contains(shipped, StringComparison.Ordinal).ShouldBeTrue(
            "test-buggen: tröskeln finns inte i det skeppade assetet — mutationen landade aldrig.");

        var mutated = json.Replace(
            shipped, $"\"coreLeadInFailAtLeast\": {failAtLeast}", StringComparison.Ordinal);

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
