using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4 STEG 9 (F4-9, ADR 0071/0074) — the deterministic CV-review engine. NO AI/LLM:
/// every verdict is a rule over the parsed CV + the versioned knowledge bank, with cited
/// evidence (CLAUDE.md §5). Golden expectations are derived from the REAL committed assets
/// (rubric.v1.0.1.json / cliche-list.v1.json / verb-mapping.v1.json) via the real loaders,
/// so the tests can never drift from the data the engine actually reads.
///
/// The internal sealed <see cref="CvReviewEngine"/> is constructed directly (Infrastructure
/// exposes internals to this assembly, parity RubricProviderTests). The engine takes
/// (IRubricProvider, IClicheLexicon, IVerbMapper, ITextAnalyzer) — NO ISpellChecker, NO
/// AppDbContext, NO ILogger (architect bound surface).
///
/// Coverage map (each success + each failure/edge case per CLAUDE.md §7):
///   - assessable criteria driven to Pass / Warn / Fail with the EXPECTED evidence channel
///     (TextSpan vs Structural);
///   - "honest NotAssessed" — every NotAssessed-v1 criterion reports NotAssessed, never a
///     fabricated Pass/Fail (CLAUDE.md §5);
///   - conditional-Period (A4/B6/B7) — NotAssessed when Period unparseable, assessed when
///     parseable;
///   - critical-fail surfacing (A1/B4/D1 → CriticalFails; C1 never fires, it is NotAssessed);
///   - scoring (category counts primary, band from data, NotAssessed excluded from the
///     denominator, Visual excludes E, ATS includes D);
///   - language dispatch (Swedish vs English CV → the right TextLanguage).
///
/// RED until ICvReviewEngine + the result types ship in Application and CvReviewEngine ships
/// internal sealed in Jobbliggaren.Infrastructure.Resumes.Review.
/// </summary>
public class CvReviewEngineTests
{
    private static CvReviewEngine NewEngine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer());

    private static async Task<CvReviewResult> ReviewAsync(
        ParsedResume resume, RenderProfile profile = RenderProfile.Ats) =>
        await NewEngine().ReviewAsync(resume, profile, TestContext.Current.CancellationToken);

    // ===============================================================
    // 0. Result envelope — version stamped, assessed/total counts honest
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldStampTheRubricVersionAndProfile_WhenCalled()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        // Bumped 1.0.0 → 1.0.1 by the reason-relocation STEG (asset renamed
        // rubric.v1.0.1.json; §2.8 patch = notAssessedReason added, no threshold change).
        result.RubricVersion.ShouldBe(RubricVersion.Parse("1.0.1"));
        result.Profile.ShouldBe(RenderProfile.Ats);
    }

    [Fact]
    public async Task ReviewAsync_ShouldEmitOneVerdictPerAssessedCriterionInProfile_WhenCalled()
    {
        // ATS assesses Profile ∈ {Both, AtsOnly} — A/B/C + D, NOT E. The verdict list
        // covers exactly those criteria; AssessedCount excludes NotAssessed-v1 ones.
        var rubric = RealRubric();
        var atsCriteria = rubric.Criteria
            .Where(c => c.Profile is RubricProfile.Both or RubricProfile.AtsOnly)
            .ToList();

        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        result.Verdicts.Select(v => v.CriterionId).ShouldBe(
            atsCriteria.Select(c => c.Id), ignoreOrder: true,
            "ATS-profilen ska ge en verdict per kriterium med Profile ∈ {Both, AtsOnly}.");

        // No E-criterion leaks into an ATS review.
        result.Verdicts.ShouldNotContain(v => v.Category == RubricCategory.VisualQuality);

        // TotalCount = criteria in profile; AssessedCount excludes NotAssessed.
        result.TotalCount.ShouldBe(atsCriteria.Count);
        result.AssessedCount.ShouldBe(
            result.Verdicts.Count(v => v.Verdict != CriterionVerdict.NotAssessed));
    }

    [Fact]
    public async Task ReviewAsync_ShouldExcludeVisualOnlyCriteria_WhenProfileIsAts()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        // D-criteria (AtsOnly) ARE present; E-criteria (VisualOnly) are NOT.
        result.Verdicts.ShouldContain(v => v.CriterionId == "D1");
        result.Verdicts.ShouldContain(v => v.CriterionId == "D6");
        result.Verdicts.ShouldNotContain(v => v.CriterionId == "E1");
    }

    [Fact]
    public async Task ReviewAsync_ShouldExcludeAtsOnlyCriteria_WhenProfileIsVisual()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Visual);

        // E-criteria (VisualOnly) ARE present; D-criteria (AtsOnly) are NOT.
        result.Verdicts.ShouldNotContain(v => v.CriterionId == "D1");
        result.Verdicts.ShouldContain(v => v.CriterionId == "E1");
    }

    // ===============================================================
    // 1. A1 Mätbara resultat (Critical) — TextSpan evidence
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassA1_WhenBulletsCarryQuantifiedResults()
    {
        var resume = Resume(experience:
        [
            Experience(rawText:
                "Ledde teamet om 8 personer och ökade konverteringen med 23 % under 2024."),
        ]);

        var result = await ReviewAsync(resume);

        VerdictOf(result, "A1").ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA1WithTextSpanEvidence_WhenNoDigitsInExperience()
    {
        // A1 atsFailSignal: "0 siffror i hela arbetslivserfarenheten". The FAIL cites the
        // offending bullet span (TextSpan), never a structural-only note.
        var resume = Resume(experience:
        [
            Experience(rawText: "Ansvarade för dagliga arbetsuppgifter och stöttade kollegor."),
        ]);

        var result = await ReviewAsync(resume);

        var a1 = Verdict(result, "A1");
        a1.Verdict.ShouldBe(CriterionVerdict.Fail);
        a1.Evidence.ShouldNotBeEmpty();
        a1.Evidence.ShouldContain(e => e is TextSpanEvidence,
            "A1-FAIL ska citera CV-spannet (TextSpanEvidence), inte bara strukturell not.");
    }

    // ===============================================================
    // 2. A2 Action verbs — TextSpan; weak verbs come from IVerbMapper
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassA2_WhenBulletsStartWithStrongVerbsFromMapping()
    {
        // Strong verbs are exactly those in verb-mapping.v1.json (golden source).
        var strong = RealVerbMapper().GetVerbMapping()
            .StrongVerbGroups.SelectMany(g => g.Verbs).First(); // e.g. "ledde"

        var resume = Resume(experience:
        [
            Experience(rawText: $"{Capitalize(strong)} teamet om 8 personer med tydligt resultat 2024."),
        ]);

        var result = await ReviewAsync(resume);

        VerdictOf(result, "A2").ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldFlagA2WithTextSpanEvidence_WhenBulletsStartWithWeakVerbs()
    {
        // A weak verb from the mapping ("var ansvarig för") at the bullet start → not a
        // strong opener. The verdict cites the offending span.
        var weak = RealVerbMapper().GetVerbMapping().WeakVerbs[0].Weak; // "var ansvarig för"

        var resume = Resume(experience:
        [
            Experience(rawText: $"{Capitalize(weak)} ett område utan tydligt resultat."),
        ]);

        var result = await ReviewAsync(resume);

        var a2 = Verdict(result, "A2");
        a2.Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
        a2.Evidence.ShouldContain(e => e is TextSpanEvidence);
    }

    // ===============================================================
    // 3. A7 Anti-klyschor — TextSpan; cliché phrases come from IClicheLexicon
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldFlagA7WithTextSpanEvidence_WhenProfileIsFullOfCliches()
    {
        // Use real cliché phrases from cliche-list.v1.json (golden). atsFailSignal:
        // "≥3 klyschor utan stöd".
        var cliches = RealClicheLexicon().GetClicheList().Entries.Take(3).Select(e => e.Phrase).ToList();
        var profile = string.Join(". ", cliches) + ".";

        var resume = Resume(profile: profile);

        var result = await ReviewAsync(resume);

        var a7 = Verdict(result, "A7");
        a7.Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
        a7.Evidence.ShouldContain(e => e is TextSpanEvidence,
            "A7 ska citera klyscha-spannet (TextSpanEvidence).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA7_WhenProfileHasNoCliches()
    {
        var resume = Resume(profile:
            "Backend-utvecklare med 8 års erfarenhet av betalsystem. Migrerade 3 plattformar till molnet 2024.");

        var result = await ReviewAsync(resume);

        VerdictOf(result, "A7").ShouldBe(CriterionVerdict.Pass);
    }

    // ===============================================================
    // 4. A8 Profiltext — length-based; TextSpan/structural on the profile
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldFailA8_WhenProfileIsMissing()
    {
        // A8 atsFailSignal: "Saknas helt". A missing profile fails.
        var resume = Resume(profile: null);

        var result = await ReviewAsync(resume);

        Verdict(result, "A8").Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    // ===============================================================
    // 5. A10 Utbildning — STRUCTURAL evidence (Education completeness)
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldUseStructuralEvidenceForA10_WhenEducationMissing()
    {
        var resume = Resume(education: []);

        var result = await ReviewAsync(resume);

        var a10 = Verdict(result, "A10");
        a10.Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
        a10.Evidence.ShouldContain(e => e is StructuralEvidence,
            "A10 är en strukturell completeness-check → StructuralEvidence.");
    }

    // ===============================================================
    // 6. B3 Kontaktuppgifter — STRUCTURAL; missing e-post → Fail
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldFailB3WithStructuralEvidence_WhenEmailMissing()
    {
        // B3 atsFailSignal: "Saknar e-post/telefon". Missing email → FAIL with a
        // StructuralEvidence observation ("e-post saknas").
        var resume = Resume(contact: new ParsedContact("Anna Andersson", null, "070-1234567", "Stockholm"));

        var result = await ReviewAsync(resume);

        var b3 = Verdict(result, "B3");
        b3.Verdict.ShouldBe(CriterionVerdict.Fail);
        b3.Evidence.ShouldContain(e => e is StructuralEvidence,
            "B3-FAIL för saknad e-post ska vara StructuralEvidence, inte ett text-span.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassB3_WhenContactComplete()
    {
        var result = await ReviewAsync(Resume(contact: CompleteContact()));

        VerdictOf(result, "B3").ShouldBe(CriterionVerdict.Pass);
    }

    // ===============================================================
    // 7. B4 Personnummer (Critical) — from PersonnummerScanOutcome
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassB4_WhenNoPersonnummerFound()
    {
        var result = await ReviewAsync(Resume(personnummer: PersonnummerScanOutcome.None));

        VerdictOf(result, "B4").ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailB4_WhenPersonnummerFound()
    {
        // B4 atsFailSignal: "Personnummer angivet — auto-fail". The engine reads the
        // PII-safe PersonnummerScanOutcome (count/kind) — NEVER the raw value or offsets.
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Personnummer 811218-9876 i CV."));

        var result = await ReviewAsync(Resume(personnummer: flagged));

        var b4 = Verdict(result, "B4");
        b4.Verdict.ShouldBe(CriterionVerdict.Fail);
        b4.Evidence.ShouldContain(e => e is StructuralEvidence,
            "B4 citerar antalet/utfallet strukturellt, aldrig råvärdet eller offsets (Inv.1).");
    }

    // ===============================================================
    // 8. B8 Filnamn — SourceFileName regex; structural
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassB8_WhenFileNameFollowsRecommendation()
    {
        var result = await ReviewAsync(Resume(sourceFileName: "CV_Anna_Andersson.pdf"));

        VerdictOf(result, "B8").ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldFlagB8_WhenFileNameIsGeneric()
    {
        // B8 atsFailSignal: "cv.pdf, document(1).pdf".
        var result = await ReviewAsync(Resume(sourceFileName: "document(1).pdf"));

        Verdict(result, "B8").Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
    }

    // ===============================================================
    // 9. D1 Filformat (Critical) — via ParseConfidence.Fallback
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassD1_WhenParseWasTextual()
    {
        // D1: a textual PDF/DOCX (Fallback != ScannedImageNoText) passes.
        var result = await ReviewAsync(Resume(confidence: ConfidentConfidence()), RenderProfile.Ats);

        VerdictOf(result, "D1").ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailD1_WhenScannedImageNoText()
    {
        // D1 atsFailSignal: "Inscannad bild-PDF". ParseConfidence.Fallback ==
        // ScannedImageNoText → Fail.
        var scanned = ParseConfidence.Failed(ParseFallbackReason.ScannedImageNoText);

        var result = await ReviewAsync(Resume(confidence: scanned), RenderProfile.Ats);

        Verdict(result, "D1").Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    // ===============================================================
    // 10. D6 Standardrubriker — heading lexicon; ATS-only, present in ATS
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldAssessD6_WhenProfileIsAts()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        Verdict(result, "D6").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    // ===============================================================
    // 11. HONEST NotAssessed v1 — never fabricated Pass/Fail (CLAUDE.md §5)
    // ===============================================================

    public static TheoryData<string> NotAssessedV1Criteria()
    {
        // The pinned/no-input NotAssessed-v1 set per the architect classification. A3 & D8
        // are ad-dependent (no ad in F4-9); A5 & C1 are pinned NotAssessedV1 in the rubric;
        // B2/B5 & D2/D3/D4/D5/D7/D9/D10 & E1–E8 are page-count/layout/font signals the
        // deterministic parse cannot see. Every one MUST report NotAssessed.
        var data = new TheoryData<string>();
        foreach (var id in new[]
        {
            "A3", "A5", "B2", "B5", "C1",
            "D2", "D3", "D4", "D5", "D7", "D8", "D9", "D10",
        })
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NotAssessedV1Criteria))]
    public async Task ReviewAsync_ShouldReportNotAssessed_ForEveryNotAssessedV1Criterion(string criterionId)
    {
        // A NotAssessed-v1 criterion is never silently mis-reported as Pass/Fail — the
        // central honesty contract (CLAUDE.md §5). Profile chosen so the criterion is in
        // scope (D-criteria are ATS-only; the rest are Both).
        var rubric = RealRubric();
        var criterion = rubric.Criteria.Single(c => c.Id == criterionId);
        var profile = criterion.Profile == RubricProfile.AtsOnly
            ? RenderProfile.Ats
            : RenderProfile.Ats; // both-criteria are also assessed in ATS

        var result = await ReviewAsync(Resume(), profile);

        var verdict = Verdict(result, criterionId);
        verdict.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            $"{criterionId} är NotAssessed-v1 och får aldrig rapporteras som Pass/Fail.");
        verdict.Evidence.ShouldBeEmpty();
        verdict.NotAssessedReason.ShouldNotBeNullOrWhiteSpace(
            $"{criterionId} ska bära ett ärligt NotAssessed-skäl, aldrig tom.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldReportEveryVisualCriterionAsNotAssessed_WhenProfileIsVisual()
    {
        // E1–E8 (Visuell kvalitet) are all layout signals the deterministic text parse
        // cannot see → NotAssessed in v1. A fully-NotAssessed category E must still be
        // emitted honestly (excluded from the blend, never faked).
        var result = await ReviewAsync(Resume(), RenderProfile.Visual);

        var visual = result.Verdicts.Where(v => v.Category == RubricCategory.VisualQuality).ToList();
        visual.ShouldNotBeEmpty();
        visual.ShouldAllBe(v => v.Verdict == CriterionVerdict.NotAssessed);
    }

    // ===============================================================
    // 11b. NotAssessed REASON comes from rubric DATA, not an inline switch
    //      (reason-relocation STEG, ADR 0071; CLAUDE.md §10/§5). The engine's
    //      old PinnedReason/NoInputReason/AdDependentCriteria members are gone —
    //      Evaluate uses criterion.NotAssessedReason ?? <civic fallback>.
    //      Exact-string asserts prove the dev-jargon switch is no longer the source.
    // ===============================================================

    // Test C — pinned NotAssessedV1 criteria (A5, C1) carry the ASSET-AUTHORED civic
    // reason, not the old "...kräver POS/NER bortom v1 (ADR 0071 OQ3)..." dev-jargon.
    [Fact]
    public async Task ReviewAsync_ShouldCarryAssetCivicReasonForA5_WhenPinnedNotAssessed()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var a5 = Verdict(result, "A5");
        a5.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        a5.NotAssessedReason.ShouldBe(
            "Vi bedömer inte karriärutveckling i den här versionen.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldCarryAssetCivicReasonForC1_WhenPinnedNotAssessed()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var c1 = Verdict(result, "C1");
        c1.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        c1.NotAssessedReason.ShouldBe(
            "Djupare stavnings- och grammatikkontroll ingår inte i den här versionen.");
    }

    // Test D — ad-dependent no-rule criteria (A3, D8) carry the ad-dependent civic reason
    // (replaces the old "matchnings-motorns ansvar, F4-5/6 — ej bedömt v1" string).
    [Fact]
    public async Task ReviewAsync_ShouldCarryAdDependentCivicReasonForA3_WhenNoRule()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var a3 = Verdict(result, "A3");
        a3.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        a3.NotAssessedReason.ShouldBe(
            "Det här bedöms mot en specifik jobbannons, inte i den allmänna granskningen.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldCarryAdDependentCivicReasonForD8_WhenNoRule()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var d8 = Verdict(result, "D8");
        d8.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d8.NotAssessedReason.ShouldBe(
            "Det här bedöms mot en specifik jobbannons, inte i den allmänna granskningen.");
    }

    // Test E — layout/no-rule criteria (e.g. E1, D9) carry the text-tolkning civic reason
    // (replaces the old "Kräver layout-/fil-metadata... — ej bedömt v1" dev-jargon).
    [Fact]
    public async Task ReviewAsync_ShouldCarryLayoutCivicReasonForE1_WhenNoRule()
    {
        // E1 (Hierarki) is VisualOnly → assess under the Visual profile.
        var result = await ReviewAsync(Resume(), RenderProfile.Visual);

        var e1 = Verdict(result, "E1");
        e1.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        e1.NotAssessedReason.ShouldBe(
            "Vi kan inte läsa det här ur en textbaserad tolkning av ditt CV.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldCarryLayoutCivicReasonForD9_WhenNoRule()
    {
        // D9 (Filstorlek) is AtsOnly, no registered rule → layout civic reason.
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var d9 = Verdict(result, "D9");
        d9.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d9.NotAssessedReason.ShouldBe(
            "Vi kan inte läsa det här ur en textbaserad tolkning av ditt CV.");
    }

    // Test F — code-side civic FALLBACK when the asset omits notAssessedReason (N-1 asset).
    // Driven via a substitute IRubricProvider whose A5 criterion has NotAssessedReason ==
    // null, proving Evaluate resolves `criterion.NotAssessedReason ?? <civic fallback>` and
    // never throws. The real v1.0.1 asset always authors the field (Test G part 2 guards
    // that), so this fallback is only reachable through an older (N-1) provider — exactly
    // the seam this test drives.
    [Fact]
    public async Task ReviewAsync_ShouldUseCivicFallback_WhenNotAssessedReasonIsNull()
    {
        var engine = new CvReviewEngine(
            FakeRubricProviderWithNullReasonOnA5(),
            RealClicheLexicon(),
            RealVerbMapper(),
            Analyzer());

        var result = await engine.ReviewAsync(
            Resume(), RenderProfile.Ats, TestContext.Current.CancellationToken);

        var a5 = Verdict(result, "A5");
        a5.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        a5.NotAssessedReason.ShouldBe(
            "Det här bedöms inte i den här versionen av granskningen.",
            "När assetet (N-1) saknar notAssessedReason ska motorn falla tillbaka på " +
            "den civila kod-defaulten, aldrig kasta eller läcka dev-jargon.");
    }

    /// <summary>
    /// A substitute <see cref="IRubricProvider"/> serving the REAL rubric but with A5's
    /// <see cref="RubricCriterion.NotAssessedReason"/> forced to null — simulating an N-1
    /// asset that pre-dates the field, so the engine's code-side civic fallback is exercised.
    /// </summary>
    private static IRubricProvider FakeRubricProviderWithNullReasonOnA5()
    {
        var real = RealRubric();
        var patched = real.Criteria
            .Select(c => c.Id == "A5" ? c with { NotAssessedReason = null } : c)
            .ToList();

        var rubric = real with { Criteria = patched };

        var provider = Substitute.For<IRubricProvider>();
        provider.GetRubric().Returns(rubric);
        return provider;
    }

    // ===============================================================
    // 12. Conditional-Period (A4/B6/B7) — NotAssessed when Period unparseable
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldNotAssessChronologyCriteria_WhenPeriodsAreUnparseable()
    {
        // A4 (gaps), B6 (date-format consistency), B7 (chronology) all need parseable
        // Period dates. With free-text/unparseable periods the engine must report
        // NotAssessed with a structural reason — never guess gaps from garbage.
        var resume = Resume(experience:
        [
            Experience(period: "någon gång på 2020-talet"),
            Experience(period: "ett tag sen"),
        ]);

        var result = await ReviewAsync(resume);

        foreach (var id in new[] { "A4", "B6", "B7" })
        {
            var verdict = Verdict(result, id);
            verdict.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
                $"{id} ska vara NotAssessed när Period inte kan parsas.");
            verdict.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ReviewAsync_ShouldAssessChronologyCriteria_WhenPeriodsParse()
    {
        // With parseable MM/YYYY periods in reverse-chronological order with no gaps,
        // A4/B6/B7 are assessed (not NotAssessed) and pass.
        var resume = Resume(experience:
        [
            Experience(period: "01/2022 – 06/2024", rawText: "Backend-utvecklare 01/2022 – 06/2024"),
            Experience(period: "08/2019 – 12/2021", rawText: "Utvecklare 08/2019 – 12/2021"),
        ]);

        var result = await ReviewAsync(resume);

        foreach (var id in new[] { "A4", "B6", "B7" })
        {
            Verdict(result, id).Verdict.ShouldNotBe(CriterionVerdict.NotAssessed,
                $"{id} ska bedömas när Period parsas.");
        }
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassB6_WhenIsoAndSlashPeriodsShareMonthGranularity()
    {
        // #420 token binding: an ISO 8601 YYYY-MM point maps to the SAME month-granularity token
        // ("MM/YYYY") as slash notation, so a CV mixing "2020-06 – 2024-03" and "01/2019 – 05/2020"
        // reads as ONE consistent date format → B6 PASS, not a spurious "blandade datumformat" WARN.
        // B6DateFormatRule verdicts on the DISTINCT FormatToken set, so the binding is decision-
        // relevant. Reverse-chronological order keeps B7 happy; only B6 is asserted here.
        var resume = Resume(experience:
        [
            Experience(period: "2020-06 – 2024-03", rawText: "Sjuksköterska 2020-06 – 2024-03"),
            Experience(period: "01/2019 – 05/2020", rawText: "Undersköterska 01/2019 – 05/2020"),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "B6").Verdict.ShouldBe(CriterionVerdict.Pass,
            "ISO YYYY-MM och MM/YYYY är samma månads-granularitet → ett datumformat → B6 PASS (#420).");
    }

    // ===============================================================
    // 13. Critical-fail surfacing — A1/B4/D1 → CriticalFails; C1 never fires
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldSurfaceA1AndB4FailsInCriticalFails_WhenBothFail()
    {
        // A1, B4, C1, D1 are CriticalFailIds in the rubric. A1 (no digits) + B4
        // (personnummer found) both fail → both surface in CriticalFails.
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Personnummer 811218-9876."));
        var resume = Resume(
            experience: [Experience(rawText: "Ansvarade för diverse uppgifter utan resultat.")],
            personnummer: flagged);

        var result = await ReviewAsync(resume, RenderProfile.Ats);

        result.CriticalFails.Select(v => v.CriterionId).ShouldContain("A1");
        result.CriticalFails.Select(v => v.CriterionId).ShouldContain("B4");
        result.CriticalFails.ShouldAllBe(v => v.Verdict == CriterionVerdict.Fail,
            "Endast FAIL-verdikt på critical-kriterier hamnar i CriticalFails.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNeverSurfaceC1InCriticalFails_BecauseC1IsNotAssessed()
    {
        // C1 (Stavning/grammatik) is a CriticalFailId BUT is pinned NotAssessedV1 — it can
        // never produce a FAIL, so it must never appear in CriticalFails (no fabricated
        // critical fail, CLAUDE.md §5).
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        result.CriticalFails.ShouldNotContain(v => v.CriterionId == "C1");
        Verdict(result, "C1").Verdict.ShouldBe(CriterionVerdict.NotAssessed);
    }

    [Fact]
    public async Task ReviewAsync_ShouldHaveEmptyCriticalFails_WhenStrongCvHasNoCriticalFailure()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        result.CriticalFails.ShouldBeEmpty(
            "Ett starkt CV utan kritiska FAIL ska ge tom CriticalFails-lista.");
    }

    // ===============================================================
    // 14. Scoring — category COUNTS primary, band from data, NotAssessed
    //     excluded from the denominator
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldExposeVerdictCountsPerCategory_WhenCalled()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        foreach (var category in result.Categories)
        {
            var inCategory = result.Verdicts.Where(v => v.Category == category.Category).ToList();
            category.PassCount.ShouldBe(inCategory.Count(v => v.Verdict == CriterionVerdict.Pass));
            category.WarnCount.ShouldBe(inCategory.Count(v => v.Verdict == CriterionVerdict.Warn));
            category.FailCount.ShouldBe(inCategory.Count(v => v.Verdict == CriterionVerdict.Fail));
            category.NotAssessedCount.ShouldBe(
                inCategory.Count(v => v.Verdict == CriterionVerdict.NotAssessed));
        }
    }

    [Fact]
    public async Task ReviewAsync_ShouldMapCategoryBandToRubricBands_WhenCalled()
    {
        // The category Band must be one of the rubric's data-driven ScoreBandLabels — the
        // engine never invents a band; it maps the category score onto rubric.Bands.
        var bandLabels = RealRubric().Bands.Select(b => b.Label).ToHashSet();

        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        result.Categories.Select(c => c.Band).ShouldAllBe(b => bandLabels.Contains(b));
    }

    [Fact]
    public async Task ReviewAsync_ShouldRankAStrongCvHigherThanAWeakCv_OnContentBand()
    {
        // Behavioural scoring check: a strong CV (quantified, action-verbs, no clichés)
        // bands at least as high on Content as a weak CV (no digits, clichés, weak verbs).
        var strong = Resume();
        var weak = Resume(
            profile: "Driven lagspelare. Resultatorienterad. Ansvarstagande.",
            experience: [Experience(rawText: "Var ansvarig för diverse uppgifter.")]);

        var strongContent = ContentBand(await ReviewAsync(strong));
        var weakContent = ContentBand(await ReviewAsync(weak));

        ((int)strongContent).ShouldBeGreaterThanOrEqualTo((int)weakContent,
            "Ett starkt CV ska inte banda lägre än ett svagt på Innehåll.");

        static ScoreBandLabel ContentBand(CvReviewResult r) =>
            r.Categories.Single(c => c.Category == RubricCategory.Content).Band;
    }

    [Fact]
    public async Task ReviewAsync_ShouldExcludeNotAssessedFromAssessedCount_WhenCalled()
    {
        // The honest denominator: AssessedCount excludes NotAssessed; TotalCount includes
        // them. A fully-NotAssessed category contributes 0 to AssessedCount.
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        result.AssessedCount.ShouldBeLessThan(result.TotalCount,
            "Det finns NotAssessed-v1-kriterier (A3/A5/...) → AssessedCount < TotalCount.");
        result.AssessedCount.ShouldBe(
            result.Verdicts.Count(v => v.Verdict != CriterionVerdict.NotAssessed));
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotExposeAnOpaqueTotalScore_WhenCalled()
    {
        // Goodhart guard (CLAUDE.md §5; parity MatchScore) — CvReviewResult exposes COUNTS
        // and per-category Bands, but NO opaque numeric total/score property. Pinned by the
        // property-name allowlist (AssessedCount/TotalCount int counts are allowed).
        var props = typeof(CvReviewResult)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToList();

        props.ShouldBe(
            ["RubricVersion", "Profile", "Categories", "Verdicts", "CriticalFails",
             "AssessedCount", "TotalCount"],
            ignoreOrder: true,
            "CvReviewResult får INTE bära en opak total/score (Goodhart, CLAUDE.md §5). " +
            $"Faktiska: [{string.Join(", ", props)}].");
    }

    // ===============================================================
    // 15. Language dispatch — Swedish vs English CV → the right TextLanguage
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldAssessLanguageCriteria_WhenCvIsSwedish()
    {
        // A Swedish CV (DetectedLanguage.Sv) → the engine routes the NLP-tier criteria
        // (C2/C3/C4/C5) through TextLanguage.Swedish and assesses them.
        var resume = Resume(detectedLanguage: ResumeLanguage.Sv);

        var result = await ReviewAsync(resume);

        Verdict(result, "C2").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed);
        Verdict(result, "C3").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    [Fact]
    public async Task ReviewAsync_ShouldAssessLanguageCriteria_WhenCvIsEnglish()
    {
        // An English CV (DetectedLanguage.En) → the engine routes NLP-tier criteria through
        // TextLanguage.English. The dispatch must not throw NotSupportedException at F4-9
        // (English is wired here per the TextLanguage contract).
        var resume = Resume(
            detectedLanguage: ResumeLanguage.En,
            profile: "Backend engineer with 8 years building payment platforms. Delivered 3 cloud migrations.",
            experience:
            [
                Experience(title: "Backend Engineer", organization: "Acme Inc",
                    period: "01/2022 – 06/2024",
                    rawText: "Led a team of 8 and increased conversion by 23% in 2024."),
            ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "C3").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed,
            "En engelsk CV ska bedömas via TextLanguage.English, inte kasta NotSupported.");
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
