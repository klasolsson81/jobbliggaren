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
/// (rubric.v2.0.0.json / cliche-list.v2.json / verb-mapping.v1.json) via the real loaders,
/// so the tests can never drift from the data the engine actually reads.
///
/// The internal sealed <see cref="CvReviewEngine"/> is constructed directly (Infrastructure
/// exposes internals to this assembly, parity RubricProviderTests). The engine takes
/// (IRubricProvider, IClicheLexicon, IVerbMapper, ITextAnalyzer, ISpellChecker,
/// ISpellingAllowlist) as of Fas 4b PR-6a (the C7 spelling criterion — an all-correct stub
/// checker keeps these tests off DSSO-vocabulary coupling), NO AppDbContext, NO ILogger.
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
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist());

    private static async Task<CvReviewResult> ReviewAsync(
        ParsedResume resume, RenderProfile profile = RenderProfile.Ats) =>
        await NewEngine().ReviewAsync(CvReviewContext.FromParsed(resume), profile, TestContext.Current.CancellationToken);

    // ===============================================================
    // 0. Result envelope — version stamped, assessed/total counts honest
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldStampTheRubricVersionAndProfile_WhenCalled()
    {
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        // Bumped 2.0.0 → 2.1.0 (#655 PR-6b: B2/D9/E2 gained geometry thresholds → minor bump,
        // RubricVersion doctrine — thresholds added, no new scored criterion). Prior: 1.2.0 →
        // 2.0.0 (#655 PR-6a: C7 spelling criterion ADDED → major bump); 1.1.0 → 1.2.0 (#654,
        // thresholds-as-data + styleOnly); #488.
        result.RubricVersion.ShouldBe(RubricVersion.Parse("2.1.0"));
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
    // 0b. Swedish evidence copy — no English enum token leaks (#478 Low)
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldNameSectionsInSwedish_WhenD6ListsDetectedHeadings()
    {
        // The default parse detects Contact + Experience. Pre-fix D6 joined the raw enum values
        // ("Contact, Experience") into the Swedish evidence; now it uses the Swedish product
        // vocabulary and never leaks the English token (§10 + §5 honesty).
        var observation = Verdict(await ReviewAsync(Resume(), RenderProfile.Ats), "D6")
            .Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

        observation.ShouldContain("kontakt");
        observation.ShouldContain("arbetslivserfarenhet");
        observation.ShouldNotContain("Experience");
        observation.ShouldNotContain("Contact");
    }

    [Fact]
    public async Task ReviewAsync_ShouldExplainDegradationInSwedish_WhenD1WarnsOnUncertainExtraction()
    {
        // D1 Warn interpolated the raw ParseFallbackReason ("ExtractionFailed") into Swedish copy.
        var resume = Resume(confidence: new ParseConfidence(
            OverallConfidenceLevel.Degraded,
            [new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["kontakt hittad"])],
            ParseFallbackReason.ExtractionFailed));

        var observation = Verdict(await ReviewAsync(resume, RenderProfile.Ats), "D1")
            .Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

        observation.ShouldContain("extraktionen misslyckades");
        observation.ShouldNotContain("ExtractionFailed");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNameEverySectionKindInSwedish_WhenAllSixHeadingsAreDetected()
    {
        // The default parse detects only Contact + Experience, so the base test exercises two of the
        // six D6 labels. Drive the full set — a confidence carrying every ParsedSectionKind at a
        // detected level — so each Swedish product-vocabulary label renders and NO English enum token
        // leaks (§10 + §5). Same guarantee as ReviewEvidenceLabelsTests, but through the real engine.
        var allDetected = new ParseConfidence(
            OverallConfidenceLevel.Confident,
            [.. Enum.GetValues<ParsedSectionKind>()
                .Select(k => new SectionConfidence(k, SectionConfidenceLevel.Confident, ["hittad"]))],
            ParseFallbackReason.None);

        var observation = Verdict(await ReviewAsync(Resume(confidence: allDetected), RenderProfile.Ats), "D6")
            .Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

        // Every Swedish label is present (literal expectation, decoupled from the label map so a
        // map-and-test co-drift cannot hide) …
        foreach (var swedish in new[]
                 { "kontakt", "profil", "arbetslivserfarenhet", "utbildning", "kompetenser", "språk" })
        {
            observation.ShouldContain(swedish);
        }

        // … and no raw enum token (Contact/Profile/Experience/Education/Skills/Languages) survives.
        foreach (var englishToken in Enum.GetNames<ParsedSectionKind>())
        {
            observation.ShouldNotContain(englishToken);
        }
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnD6InSwedishWithoutLeakingAnEnumToken_WhenNoSectionsAreDetected()
    {
        // The zero-detected branch (creative headings) is honest Swedish copy with no enum
        // interpolation. Pin it so the Warn branch stays covered and no ParsedSectionKind token can
        // creep into it under a later refactor.
        var noneDetected = new ParseConfidence(
            OverallConfidenceLevel.Degraded, [], ParseFallbackReason.NoSectionsDetected);

        var d6 = Verdict(await ReviewAsync(Resume(confidence: noneDetected), RenderProfile.Ats), "D6");
        var observation = d6.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

        d6.Verdict.ShouldBe(CriterionVerdict.Warn);
        observation.ShouldContain("Inga standardsektioner");
        foreach (var englishToken in Enum.GetNames<ParsedSectionKind>())
        {
            observation.ShouldNotContain(englishToken);
        }
    }

    [Fact]
    public async Task ReviewAsync_ShouldExplainDegradationInSwedish_WhenD1WarnsOnSuspectEncoding()
    {
        // Sibling to the ExtractionFailed test: EncodingSuspect is the SECOND arm of D1's Warn pattern
        // and a DIFFERENT label. Pre-fix the raw enum ("EncodingSuspect") was interpolated into the
        // Swedish copy; now the degradation is explained in Swedish (§10 + §5).
        var resume = Resume(confidence: new ParseConfidence(
            OverallConfidenceLevel.Degraded,
            [new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["kontakt hittad"])],
            ParseFallbackReason.EncodingSuspect));

        var d1 = Verdict(await ReviewAsync(resume, RenderProfile.Ats), "D1");
        var observation = d1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

        d1.Verdict.ShouldBe(CriterionVerdict.Warn);
        observation.ShouldContain("teckenkodningen ser felaktig ut");
        observation.ShouldNotContain("EncodingSuspect");
    }

    // ===============================================================
    // 1. A1 Mätbara resultat (Critical) — TextSpan evidence
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassA1_WhenBulletsCarryQuantifiedResults()
    {
        var resume = Resume(experience:
        [
            Experience(bullets:
                ["Ledde teamet om 8 personer och ökade konverteringen med 23 % under 2024."]),
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
            Experience(bullets: ["Ansvarade för dagliga arbetsuppgifter och stöttade kollegor."]),
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
            Experience(bullets: [$"{Capitalize(strong)} teamet om 8 personer med tydligt resultat 2024."]),
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
            Experience(bullets: [$"{Capitalize(weak)} ett område utan tydligt resultat."]),
        ]);

        var result = await ReviewAsync(resume);

        var a2 = Verdict(result, "A2");
        a2.Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
        a2.Evidence.ShouldContain(e => e is TextSpanEvidence);
    }

    // ===============================================================
    // 2b. #487 — the scored bullet unit is the DESCRIPTION, not the whole
    //     entry block (header + period). A1/A2/A6 read real bullet lines;
    //     employment dates are masked out of the measurable-digit test.
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldFailA1_WhenTheOnlyDigitsAreTheEmploymentDates()
    {
        // #487(a): the entry's date row ("2013–2021") is not a measurable result. A CV whose
        // description carries no quantification must FAIL the critical A1 — pre-fix it PASSED
        // vacuously because ContainsDigit saw the period's digits in the whole-block "bullet".
        var resume = Resume(experience:
        [
            Experience(title: "Verksamhetsutvecklare", organization: "Region Skåne",
                period: "2013–2021",
                bullets: ["Ansvarade för dagliga arbetsuppgifter och stöttade kollegor."]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Fail,
            "A1 får inte passera på anställningsdatumets siffror — datumet är inte ett mätbart resultat (#487).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA2_WhenEveryBulletOpensWithAStrongVerb_EvenWithATitleHeader()
    {
        // #487(b): pre-fix A2 read the entry's first word = the job title, never a verb, and
        // FAILED citing the title even when every bullet opens with a strong verb. With real
        // bullet lines A2 PASSES.
        var strong = RealVerbMapper().GetVerbMapping()
            .StrongVerbGroups.SelectMany(g => g.Verbs).First();
        var resume = Resume(experience:
        [
            Experience(title: "Backend-utvecklare", organization: "Acme AB", period: "2021–2024",
                bullets:
                [
                    $"{Capitalize(strong)} teamet om 8 personer.",
                    $"{Capitalize(strong)} en ny betalningsplattform.",
                ]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A2").Verdict.ShouldBe(CriterionVerdict.Pass,
            "A2 ska läsa punktradernas verb, inte jobbtiteln i rubrikraden (#487).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotCountDatesAsConcreteArtefactsInA6()
    {
        // #487(c): A6.IsConcrete counted the date's digits (and the header's capitalised
        // organisation) as a "concrete artefact". A generic description whose only digits are
        // the dates and whose only capitalised word is the header must NOT pass A6.
        var resume = Resume(experience:
        [
            Experience(title: "Handläggare", organization: "Myndigheten", period: "2015–2020",
                bullets: ["Skötte löpande ärenden och deltog i möten."]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A6").Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
    }

    [Fact]
    public async Task ReviewAsync_ShouldReportNoDescriptionBulletsReasonForA1_WhenEntriesAreHeaderOnly()
    {
        // #487(d): an entry that is only a header+period (no description lines) yields no
        // scorable bullets → A1/A2/A6 report NotAssessed honestly (never a fabricated
        // Pass/Fail). The reason distinguishes "experience stated but no description lines"
        // from "no experience at all" (CTO Q2d honesty).
        var resume = Resume(experience:
        [
            Experience(title: "Konsult", organization: "Firman", period: "2018–2022", bullets: []),
        ]);

        var result = await ReviewAsync(resume);

        foreach (var id in new[] { "A1", "A2", "A6" })
        {
            var verdict = Verdict(result, id);
            verdict.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
                $"{id} ska vara NotAssessed när posterna saknar beskrivande punkter (#487).");
            // Erfarenhet finns men utan punkter → skälet ska skilja sig från 'ingen erfarenhet'.
            verdict.NotAssessedReason.ShouldNotBeNull();
            verdict.NotAssessedReason!.ShouldContain("saknar beskrivande punkter");
        }
    }

    [Fact]
    public async Task ReviewAsync_ShouldReportNoExperienceReasonForA1_WhenThereIsNoExperienceAtAll()
    {
        // #487(d) sibling: no experience entries at all → the honest reason names "ingen
        // arbetslivserfarenhet", distinct from the header-only "saknar beskrivande punkter".
        var resume = Resume(experience: []);

        var result = await ReviewAsync(resume);

        var a1 = Verdict(result, "A1");
        a1.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        // Utan erfarenhetsposter ska skälet skilja sig från 'saknar beskrivande punkter' (#487).
        a1.NotAssessedReason.ShouldNotBeNull();
        a1.NotAssessedReason!.ShouldContain("Ingen arbetslivserfarenhet");
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnA1_WhenSomeButNotAllBulletsAreQuantified()
    {
        // #487 mid-branch (0 < quantified < bullets → Warn) on the Critical A1 criterion.
        var resume = Resume(experience:
        [
            Experience(bullets:
            [
                "Ökade konverteringen med 23 procent.",
                "Ansvarade för teamets dagliga arbete.",
            ]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Warn,
            "En kvantifierad och en icke-kvantifierad punkt → A1 Warn (#487).");
    }

    // ===============================================================
    // 1b. A1 second Fail clause — ">50 % av punkterna saknar mätbarhet"
    //     is a CRITICAL Fail, not a Warn (#489)
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldFailA1_WhenMoreThanHalfTheBulletsLackMeasurability()
    {
        // #489 second Fail clause: rubric atsFailSignal "... ELLER >50 % av punkterna saknar
        // mätbarhet". Two of three bullets unquantified (0.67 > 0.50) → the Critical A1 FAILS.
        // Pre-fix ANY missing bullet was only a Warn, suppressing this critical-fail surface.
        var resume = Resume(experience:
        [
            Experience(bullets:
            [
                "Ökade konverteringen med 23 procent.",
                "Ansvarade för teamets dagliga arbete.",
                "Deltog i möten och interna samarbeten.",
            ]),
        ]);

        var a1 = Verdict(await ReviewAsync(resume), "A1");
        a1.Verdict.ShouldBe(CriterionVerdict.Fail,
            "Över hälften av punkterna utan mätbarhet → A1 (Kritisk) Fail (#489).");
        // §5: the Fail cites the offending UNQUANTIFIED bullet, not the quantified one.
        var quote = a1.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote;
        quote.ShouldContain("Ansvarade för teamets dagliga arbete");
        quote.ShouldNotContain("Ökade");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA1_WhenMostBulletsCarryOnlyAnEmploymentDate()
    {
        // #487 × #489: a bullet whose only digit is a masked employment date counts as UNQUANTIFIED
        // toward the >50 % clause — three date-only bullets against one real metric (3/4) → Fail via
        // the SECOND clause (not the "0 siffror" first clause, since one real metric exists).
        var resume = Resume(experience:
        [
            Experience(bullets:
            [
                "Ökade försäljningen med 20 procent.",
                "Arbetade under 2013–2021 med förvaltning.",
                "Deltog i projektet 2019.",
                "Ansvarade sedan 2020 för rutiner.",
            ]),
        ]);

        Verdict(await ReviewAsync(resume), "A1").Verdict.ShouldBe(CriterionVerdict.Fail,
            "Datum-only-punkter räknas som okvantifierade → 3/4 > 50 % → A1 Fail (#487/#489).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldSurfaceTheA1SecondClauseFailInCriticalFails()
    {
        // A1 is a CriticalFailId — the >50 % Fail must surface in CriticalFails, which is the whole
        // point of #489 (the suppressed critical-fail surface).
        var resume = Resume(experience:
        [
            Experience(bullets:
            [
                "Ökade konverteringen med 23 procent.",
                "Ansvarade för dagliga uppgifter.",
                "Deltog i möten.",
            ]),
        ]);

        var result = await ReviewAsync(resume);
        result.CriticalFails.Select(v => v.CriterionId).ShouldContain("A1");
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnA1_WhenExactlyHalfTheBulletsLackMeasurability()
    {
        // Boundary: exactly 50 % missing is NOT > 50 % → Warn, not Fail (strict boundary, parity the
        // existing 1-of-2 Warn case). Pins that the clause fires strictly ABOVE half.
        var resume = Resume(experience:
        [
            Experience(bullets:
            [
                "Ökade försäljningen med 20 procent.",
                "Sänkte kostnaden med 15 procent.",
                "Ansvarade för dagliga uppgifter.",
                "Deltog i teammöten regelbundet.",
            ]),
        ]);

        Verdict(await ReviewAsync(resume), "A1").Verdict.ShouldBe(CriterionVerdict.Warn,
            "Exakt 50 % saknad mätbarhet är inte > 50 % → A1 Warn (#489-gränsen).");
    }

    [Fact]
    public async Task ReviewAsync_A1MissingRatioShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // Golden drift-guard (#489, parity A7/C3): derive the A1 missing-measurability Fail ratio
        // from the versioned rubric prose (atsFailSignal ">50 % av punkterna saknar mätbarhet").
        var a1 = RealRubric().Criteria.Single(c => c.Id == "A1");
        var failPercent = PercentInSignal(a1.AtsFailSignal!);   // 50 (the "%"-bearing number, not the "0")
        var failRatio = failPercent / 100.0;                    // 0.50

        // 10 bullets: missingAbove/10 strictly exceeds the ratio; missingAt/10 sits at it.
        var missingAbove = (int)Math.Floor(failRatio * 10) + 1;     // 6 → 0.60 > 0.50
        var missingAt = (int)Math.Floor(failRatio * 10);            // 5 → 0.50, not > 0.50

        Verdict(await ReviewAsync(BulletsResume(missingAbove, 10)), "A1").Verdict
            .ShouldBe(CriterionVerdict.Fail, $"{missingAbove}/10 saknad > {failRatio:0.0#} → Fail.");
        Verdict(await ReviewAsync(BulletsResume(missingAt, 10)), "A1").Verdict
            .ShouldNotBe(CriterionVerdict.Fail, $"{missingAt}/10 saknad = {failRatio:0.0#} → inte Fail.");

        // `total` bullets, `missing` without a metric and the rest with one.
        static ParsedResume BulletsResume(int missing, int total)
        {
            var bullets = new List<string>();
            for (var i = 0; i < total; i++)
            {
                bullets.Add(i < missing ? "Ansvarade för dagliga uppgifter" : "Ökade resultatet med 20 procent");
            }

            return Resume(experience: [Experience(bullets: [.. bullets])]);
        }
    }

    // ── #487 date-MASKING isolation (StripDates): an inline date inside a bullet (not a
    // standalone period line, so it survives DescriptionLines) must not count as a metric.
    // These are the vectors that go RED if ContainsMeasurableDigit degrades to ContainsDigit.

    [Fact]
    public async Task ReviewAsync_ShouldFailA1_WhenTheOnlyDigitInABulletIsAnInlineYear()
    {
        var resume = Resume(experience:
        [
            Experience(bullets: ["Migrerade det gamla 1998-systemet till en ny plattform."]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Fail,
            "Ett inline-år är inte ett mätbart resultat — datumet maskas ur siffertestet (#487).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA1_WhenTheOnlyDigitsInABulletAreAnInlineDateRange()
    {
        var resume = Resume(experience:
        [
            Experience(bullets: ["Arbetade under 2013–2021 med löpande förvaltning."]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Fail,
            "Ett inline datumintervall maskas — inte ett mätbart resultat (#487).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA1_WhenABulletHasARealMetricAlongsideAMaskedDate()
    {
        // The mask must not eat a real metric: "12 procent" survives while the year is masked.
        var resume = Resume(experience:
        [
            Experience(bullets: ["Migrerade 1998-systemet och sänkte driftskostnaden med 12 procent."]),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Pass,
            "En riktig metrik ska räknas även när ett årtal i samma punkt maskas (#487).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotTreatAnInlineDateAsAConcreteArtefactInA6()
    {
        // A6.IsConcrete reuses the date-masked digit test — an inline year is not a concrete
        // artefact (and there is no capitalised named system in this bullet).
        var resume = Resume(experience:
        [
            Experience(bullets: ["deltog i det stora 2015-projektet och bidrog där det behövdes."]),
        ]);

        var result = await ReviewAsync(resume);

        // Inline-år räknas inte som konkret artefakt i A6 (#487).
        Verdict(result, "A6").Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
    }

    [Fact]
    public async Task ReviewAsync_ShouldMaskABareFourDigitYearEvenWhenItIsACount_DocumentedV1Tradeoff()
    {
        // Documented deterministic v1 tradeoff (CTO date-masking bind, #487): DatePatterns.Year()
        // masks any bare 1900–2099 number, so a rare count that happens to be a four-digit year
        // ("2000 ärenden") is masked like a date and A1 does not count it — honest-absent over
        // confidently-wrong (ADR 0071: a lone four-digit year is far more often a date than a
        // metric). A count OUTSIDE that band ("2500 ärenden") is unaffected. Pinned so the
        // tradeoff is visible and intentional, never a silent mis-report.
        var masked = Resume(experience: [Experience(bullets: ["Hanterade 2000 ärenden per år."])]);
        var unmasked = Resume(experience: [Experience(bullets: ["Hanterade 2500 ärenden per år."])]);

        Verdict(await ReviewAsync(masked), "A1").Verdict.ShouldBe(CriterionVerdict.Fail);
        Verdict(await ReviewAsync(unmasked), "A1").Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldExcludeAnOwnLineOrganisationFromBullets_WhenTitleCompanyDatesLayout()
    {
        // Genuine segmenter output where the organisation sits on its OWN line (the
        // "Title\nCompany\nDates" layout) — the organisation line is not a description bullet,
        // so A2 reads the real bullet's verb and A1 sees the real metric (#487, DescriptionLines
        // org-drop branch).
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Backend-utvecklare
            Acme AB
            2013 - 2021
            Ledde teamet om 8 personer och ökade konverteringen med 23 procent.
            """;

        var resume = ResumeFromCvText(cv);

        var result = await ReviewAsync(resume);

        Verdict(result, "A2").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Organisationsraden (Acme AB) är ingen punkt — A2 ska läsa punktens verb (#487).");
        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotVacuouslyPassA1_WhenFedRealSegmenterOutput()
    {
        // #487 format-test (epic recommendation): feed genuine HeadingDrivenResumeSegmenter
        // output — NOT hand-crafted rawText — through the engine. The header line carries the
        // job title and the period sits on its own line; a description without quantification
        // must not pass the critical A1 on the date row alone, A2 must read the bullet verb (a
        // weak "ansvarade för" opener → not a Fail here since it IS a strong-mapping verb, so
        // assert only that A2 is assessed), and A6 must not pass on the date/header alone.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            2013 - 2021
            Skötte löpande uppgifter och stöttade kollegor i det dagliga.
            """;

        var resume = ResumeFromCvText(cv);

        var result = await ReviewAsync(resume);

        Verdict(result, "A1").Verdict.ShouldBe(CriterionVerdict.Fail,
            "Segmenterar-utdata utan kvantifiering ska inte passera A1 på datumraden (#487 format-test).");
        // A6 ska inte passera på datum/rubrik när punkten saknar konkret artefakt (#487 format-test).
        Verdict(result, "A6").Verdict.ShouldBeOneOf(CriterionVerdict.Warn, CriterionVerdict.Fail);
        Verdict(result, "A2").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed,
            "A2 ska bedömas på den riktiga punkten från segmenterar-utdatan (#487 format-test).");
    }

    // ===============================================================
    // 3. A7 Anti-klyschor — kind==Cliche only, word-bounded, thresholds
    //    reconciled with the rubric prose (#489/#490/#496)
    // ===============================================================

    // The kind==Cliche / kind==SoftSkill phrases from the REAL committed asset (golden source):
    // A7 owns the clichés, A9 owns the soft-skill adjectives — one phrase, one verdict (#490).
    private static List<string> Phrases(ClicheKind kind) =>
        RealClicheLexicon().GetClicheList().Entries
            .Where(e => e.Kind == kind)
            .Select(e => e.Phrase)
            .ToList();

    [Fact]
    public async Task ReviewAsync_ShouldFailA7WithTextSpanEvidence_WhenProfileHasThreeOrMoreCliches()
    {
        // Rubric A7 atsFailSignal: "≥3 klyschor utan stöd". Three real kind==Cliche phrases → Fail,
        // citing the offending span (TextSpan).
        var profile = string.Join(". ", Phrases(ClicheKind.Cliche).Take(3)) + ".";

        var a7 = Verdict(await ReviewAsync(Resume(profile: profile)), "A7");

        a7.Verdict.ShouldBe(CriterionVerdict.Fail);
        a7.Evidence.ShouldContain(e => e is TextSpanEvidence,
            "A7 ska citera klyscha-spannet (TextSpanEvidence).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnA7_WhenProfileHasExactlyTwoCliches()
    {
        // The band between the rubric's "<2" Pass and "≥3" Fail is Warn (exactly 2 clichés).
        var profile = string.Join(". ", Phrases(ClicheKind.Cliche).Take(2)) + ".";

        Verdict(await ReviewAsync(Resume(profile: profile)), "A7").Verdict.ShouldBe(CriterionVerdict.Warn);
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA7_WhenProfileHasExactlyOneCliche()
    {
        // #489 threshold reconcile: rubric atsPassSignal is "<2 förekomster" → a SINGLE cliché
        // PASSES. Pre-fix the code passed only on 0 hits and Warned on 1, contradicting the
        // versioned rubric it claims to implement.
        var oneCliche = Phrases(ClicheKind.Cliche)[0];

        var a7 = Verdict(await ReviewAsync(Resume(profile: $"{oneCliche} och mycket annat.")), "A7");
        a7.Verdict.ShouldBe(CriterionVerdict.Pass,
            "En enda klyscha är under rubrikens <2-gräns → A7 Pass (#489).");
        // The passing single cliché is still CITED (the rule cites it for §5 explainability, never
        // a hidden flag) — pin the span so the "Pass-with-evidence" branch is covered.
        a7.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote.ShouldBe(oneCliche);
    }

    [Fact]
    public async Task ReviewAsync_ShouldCiteTheVerbatimOriginalCaseCliche_WhenProseIsLowercased()
    {
        // Inv.2: the cited span quotes the CV's OWN casing (verbatim), not the lexicon's — a
        // lowercased "brinner för" in the CV is cited as written, at the word-bounded occurrence.
        var lowerCliche = Phrases(ClicheKind.Cliche)[0].ToLowerInvariant();

        var a7 = Verdict(await ReviewAsync(Resume(profile: $"Jag {lowerCliche} kvalitet i allt.")), "A7");
        a7.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote.ShouldBe(lowerCliche,
            "Citatet ska vara CV:ts egen gemena skrivning, inte lexikonets versalisering (Inv.2).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA7_WhenProfileHasNoCliches()
    {
        var resume = Resume(profile:
            "Backend-utvecklare med 8 års erfarenhet av betalsystem. Migrerade 3 plattformar till molnet 2024.");

        VerdictOf(await ReviewAsync(resume), "A7").ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotCountACliche_WhenItIsOnlyASubstringOfALongerWord()
    {
        // #490/#496 word boundary: "Resultatorienterad" must not match inside "resultatorienterade";
        // three such near-misses must NOT trip the ≥3 Fail. Pre-fix naive Contains counted all three.
        var resume = Resume(profile:
            "Levererade resultatorienterade, lösningsorienterade och kunddrivna insatser i teamet.");

        Verdict(await ReviewAsync(resume), "A7").Verdict.ShouldBe(CriterionVerdict.Pass,
            "En klyscha som bara är en substräng i ett längre ord ska inte räknas (#490/#496).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotDoublePunishTheSamePhraseAcrossA7AndA9()
    {
        // #490 double-punishment: a soft-skill adjective ("Social") is A9's domain, NOT A7's; a
        // cliché ("Brinner för") is A7's, NOT A9's. Pre-fix both rules reused the whole lexicon, so
        // one phrase drew two simultaneous verdicts.
        var soft = Resume(profile: $"{Phrases(ClicheKind.SoftSkill)[0]} och trevlig i teamet.");
        var cliche = Resume(profile: $"{Phrases(ClicheKind.Cliche)[0]} allt jag gör.");

        // A soft-skill phrase alone leaves A7 clean (0 clichés → Pass); the cliché alone leaves A9
        // clean (0 soft adjectives → Pass).
        Verdict(await ReviewAsync(soft), "A7").Verdict.ShouldBe(CriterionVerdict.Pass);
        Verdict(await ReviewAsync(cliche), "A9").Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    // ===============================================================
    // 3b. A9 Soft skills underbyggda — kind==SoftSkill only, "backed" means
    //     a MEASURABLE example in the SAME sentence, not any digit (#490)
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldFailA9_WhenProfileIsAnUnbackedAdjectiveList()
    {
        // Rubric A9 atsFailSignal: "Adjektivlista utan exempel". Two+ soft adjectives with no nearby
        // example → Fail, citing the offending adjective span.
        var profile = $"{Phrases(ClicheKind.SoftSkill)[0]}. {Phrases(ClicheKind.SoftSkill)[1]}.";

        var a9 = Verdict(await ReviewAsync(Resume(profile: profile)), "A9");
        a9.Verdict.ShouldBe(CriterionVerdict.Fail);
        a9.Evidence.ShouldContain(e => e is TextSpanEvidence);
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA9_EvenWhenAnEmploymentDateExistsElsewhereInTheCv()
    {
        // #490 dead-branch fix: the old "ContainsDigit over ALL prose" check was satisfied by any
        // employment date, so A9's Fail branch was effectively dead — an adjective list with a dated
        // job always Warned. Now "backed" means a MEASURABLE example in the SAME sentence (dates
        // masked, #487), so this must FAIL.
        var resume = Resume(
            profile: "Social och trevlig kollega. Mycket noggrann i mitt arbete.",
            experience: [Experience(period: "2013–2021", rawText: "Handläggare 2013–2021\nSkötte löpande ärenden.")]);

        Verdict(await ReviewAsync(resume), "A9").Verdict.ShouldBe(CriterionVerdict.Fail,
            "Ett anställningsdatum någon annanstans styrker inte adjektiven i profilen (#490).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnA9AndCiteTheUnbackedAdjective_WhenExactlyOneIsUnbacked()
    {
        // The Warn boundary (exactly one unbacked adjective) + the citation redirect: the span cites
        // the UNSUPPORTED adjective ("Noggrann"), not the first soft hit ("Social", which IS backed).
        var resume = Resume(profile: "Social med 12 genomförda kundmöten. Noggrann i mitt arbete.");

        var a9 = Verdict(await ReviewAsync(resume), "A9");
        a9.Verdict.ShouldBe(CriterionVerdict.Warn,
            "Ett backat + ett obackat adjektiv → A9 Warn (#490).");
        a9.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote.ShouldBe("Noggrann",
            "Citatet ska peka på det obestyrkta adjektivet, inte det backade (#490).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA9_WhenTheOnlyNearbyDigitIsAnEmploymentDateInTheSameSentence()
    {
        // #487 masking inside the SAME sentence: a date SITTING NEXT TO the adjective ("Social sedan
        // 2015") is not a measurable example — StripDates masks it, so the adjective stays unbacked.
        // Isolates the masking (remove StripDates and 2015/2018 would falsely back the adjectives).
        var resume = Resume(profile: "Social sedan 2015. Noggrann sedan 2018.");

        Verdict(await ReviewAsync(resume), "A9").Verdict.ShouldBe(CriterionVerdict.Fail,
            "Ett datum bredvid adjektivet styrker det inte — datumet maskas (#487/#490).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA9_WhenEachAdjectiveHasAMeasurableExampleInTheSameSentence()
    {
        // Rubric A9 atsPassSignal: soft skills mentioned ONLY with a concrete example. Each adjective
        // sits in a sentence carrying a real metric (12, 300) → all backed → Pass.
        var resume = Resume(profile:
            "Social med 12 genomförda kundmöten. Noggrann med noll fel i 300 granskade fakturor.");

        Verdict(await ReviewAsync(resume), "A9").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Varje adjektiv styrks av en mätbar siffra i samma mening → A9 Pass (#490).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA9_WhenAnAdjectiveIsOnlyASubstringOfALongerWord()
    {
        // #490/#496 word boundary: "Social" must not match inside "socialtjänsten"/"socialförvaltning";
        // no soft adjective is really present → A9 Pass.
        var resume = Resume(profile: "Arbetar inom socialtjänsten och socialförvaltningen sedan länge.");

        Verdict(await ReviewAsync(resume), "A9").Verdict.ShouldBe(CriterionVerdict.Pass,
            "'Social' som substräng i 'socialtjänsten' är inget personlighetsadjektiv (#490/#496).");
    }

    [Fact]
    public async Task ReviewAsync_A7ThresholdsShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // Golden drift-guard (#489): A7's numeric thresholds are DERIVED from the versioned rubric
        // prose, not from a hardcoded expectation — so code that drifts from the rubric it claims to
        // implement fails CI (the audit's whole finding: the code's threshold had silently diverged
        // from the versioned data). atsPassSignal "<2 förekomster" is the Pass ceiling; atsFailSignal
        // "≥3 klyschor" is the Fail floor; the strict band between them is Warn.
        var a7 = RealRubric().Criteria.Single(c => c.Id == "A7");
        var passCeiling = FirstInt(a7.AtsPassSignal!);   // the N in "<N förekomster"
        var failFloor = FirstInt(a7.AtsFailSignal!);     // the M in "≥M klyschor"

        passCeiling.ShouldBeLessThan(failFloor, "rubrikens Pass-tak måste ligga under Fail-golvet.");

        var cliches = Phrases(ClicheKind.Cliche);
        failFloor.ShouldBeLessThanOrEqualTo(cliches.Count, "assetet måste bära nog med klyschor för testet.");

        // Strictly under the "<N" ceiling → Pass.
        Verdict(await ReviewAsync(WithCliches(passCeiling - 1)), "A7").Verdict
            .ShouldBe(CriterionVerdict.Pass, $"färre än {passCeiling} klyschor → rubrikens Pass.");

        // At/over the "≥M" floor → Fail.
        Verdict(await ReviewAsync(WithCliches(failFloor)), "A7").Verdict
            .ShouldBe(CriterionVerdict.Fail, $"minst {failFloor} klyschor → rubrikens Fail.");

        // The strict band between the two thresholds → Warn.
        for (var n = passCeiling; n < failFloor; n++)
        {
            Verdict(await ReviewAsync(WithCliches(n)), "A7").Verdict.ShouldBe(CriterionVerdict.Warn,
                $"{n} klyschor ligger i Warn-bandet mellan rubrikens <{passCeiling} och ≥{failFloor}.");
        }

        ParsedResume WithCliches(int count) =>
            Resume(profile: string.Join(". ", Phrases(ClicheKind.Cliche).Take(count)) + ".");
    }

    // The first integer embedded in a rubric prose signal ("<2 …" → 2, "≥3 …" → 3).
    private static int FirstInt(string prose) =>
        int.Parse(
            new string(prose.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);

    // The integer that bears the "%" sign in a rubric prose signal (">50 % ..." → 50), so a signal
    // whose FIRST number is not the percentage ("0 siffror ... ELLER >50 % ...") is read correctly.
    private static int PercentInSignal(string prose)
    {
        var pct = prose.IndexOf('%', StringComparison.Ordinal);
        var i = pct - 1;
        while (i >= 0 && !char.IsDigit(prose[i]))
        {
            i--;   // skip the space between the number and the % sign
        }

        var end = i + 1;
        while (i >= 0 && char.IsDigit(prose[i]))
        {
            i--;   // walk back over the digits
        }

        return int.Parse(
            prose.AsSpan(i + 1, end - (i + 1)), provider: System.Globalization.CultureInfo.InvariantCulture);
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

    [Fact]
    public async Task ReviewAsync_ShouldFailA8_WhenProfileExceedsTheWordLimit()
    {
        // #489 Warn-where-Fail: rubric atsFailSignal "... ELLER >100 ord ...". An over-long profile
        // FAILS — pre-fix it was only a Warn, contradicting the versioned rubric.
        var resume = Resume(profile: string.Join(" ", Enumerable.Repeat("erfarenhet", 101)));

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Fail,
            ">100 ord är en rubrik-Fail, inte en Warn (#489).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA8_WhenProfileIsABareAdjectiveList()
    {
        // #489: rubric atsFailSignal "... ELLER ren adjektivlista ...". A short profile dominated by
        // curated soft-skill adjectives with no concrete example FAILS — pre-fix such a 3-word
        // profile PASSED vacuously.
        var resume = Resume(profile: "Social, noggrann, flexibel, stresstålig.");

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Fail,
            "En ren adjektivlista av personlighetsadjektiv → A8 Fail (#489).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA8_WhenAdjectivesSitInARealSummaryWithAConcreteExample()
    {
        // The adjective-list Fail must NOT catch a real summary that HAPPENS to use a couple of
        // soft-skill words but carries a concrete example (the measurable digit guard).
        var resume = Resume(profile:
            "Erfaren och social projektledare, noggrann i uppföljning av 12 projekt under 2024.");

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Adjektiv i en riktig mening med konkret exempel ska inte flaggas som ren adjektivlista.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA8_WhenProfileIsAReasonableSummary()
    {
        // A normal 2-sentence summary within the word limit and not an adjective list → Pass.
        var resume = Resume(profile:
            "Erfaren backend-utvecklare med 8 års erfarenhet av betalsystem. "
            + "Levererade 3 plattformsmigrationer under 2024 med hög driftsäkerhet.");

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task ReviewAsync_A8WordLimitShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // Golden drift-guard (#489, parity A7/A1/C3): derive the A8 word limit from the versioned
        // rubric prose (atsFailSignal ">100 ord") — a profile at the limit Passes, just over Fails.
        var a8 = RealRubric().Criteria.Single(c => c.Id == "A8");
        var wordLimit = FirstInt(a8.AtsFailSignal!);   // 100

        Verdict(await ReviewAsync(Resume(profile: string.Join(" ", Enumerable.Repeat("ord", wordLimit)))), "A8")
            .Verdict.ShouldNotBe(CriterionVerdict.Fail, $"{wordLimit} ord (vid gränsen) → inte Fail.");
        Verdict(await ReviewAsync(Resume(profile: string.Join(" ", Enumerable.Repeat("ord", wordLimit + 1)))), "A8")
            .Verdict.ShouldBe(CriterionVerdict.Fail, $"{wordLimit + 1} ord (> gränsen) → Fail.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailA8_WhenProfileIsAnObjectiveStatement()
    {
        // #489 fourth Fail clause: rubric atsFailSignal "... ELLER \"Objective: To obtain...\"". A
        // Swedish CV opening with the English "Objective" heading is the USA objective-statement
        // anti-pattern (completeness — closes the A8↔rubric reconcile per the agent review).
        var resume = Resume(profile: "Objective: To obtain a challenging position in software engineering.");

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Fail,
            "\"Objective:\"-USA-stil är en rubrik-Fail (#489).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA8_WhenSoftAdjectivesAreNotMoreThanHalfTheWords()
    {
        // Isolates the "half the words" dominance guard: two curated soft adjectives inside a longer
        // real sentence (not dominated by them, no digit) is NOT a bare list → Pass. Removing the
        // `softAdjectives * 2 >= words` guard would wrongly Fail this on the Medium A8 criterion.
        var resume = Resume(profile:
            "Jag är en social och noggrann person som trivs med att arbeta i grupp och ta stort ansvar.");

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Ett par mjuka adjektiv i en riktig mening är ingen ren adjektivlista (#489-gränsen).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA8_WhenOnlyOneSoftAdjectiveIsPresent()
    {
        // Isolates the `softAdjectives >= 2` count guard: a single soft adjective (no digit) is NOT a
        // list → Pass. Lowering the guard to >= 1 would wrongly Fail this.
        var resume = Resume(profile: "Jag är en social medarbetare med lång erfarenhet inom vård och omsorg.");

        Verdict(await ReviewAsync(resume), "A8").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Ett enda mjukt adjektiv gör inte profilen till en ren adjektivlista (#489-gränsen).");
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

    [Fact]
    public async Task ReviewAsync_ShouldWarnB4_WhenPersonnummerOnlyInFileName()
    {
        // #426: a personnummer in the FILENAME (body clean) is a Warn, not a Fail — it does not
        // block promotion; B4 prescribes a rename. The outcome carries FoundInFileName only.
        var fileNameOnly = PersonnummerScanOutcome.FromMatches([], foundInFileName: true);

        var result = await ReviewAsync(Resume(personnummer: fileNameOnly));

        var b4 = Verdict(result, "B4");
        b4.Verdict.ShouldBe(CriterionVerdict.Warn);
        var evidence = b4.Evidence.OfType<StructuralEvidence>().ShouldHaveSingleItem();
        evidence.Observation.ShouldContain("filnamn"); // prescribes the rename remedy
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailB4_WithFileNameNote_WhenPersonnummerInBothBodyAndFileName()
    {
        // Body pnr dominates (Fail), but the observation also flags the filename so the user
        // fixes both. Count reflects the BODY only (the filename is a flag, not a count).
        var both = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Personnummer 811218-9876 i CV."), foundInFileName: true);

        var result = await ReviewAsync(Resume(personnummer: both));

        var b4 = Verdict(result, "B4");
        b4.Verdict.ShouldBe(CriterionVerdict.Fail);
        var evidence = b4.Evidence.OfType<StructuralEvidence>().ShouldHaveSingleItem();
        evidence.Observation.ShouldContain("filnamn"); // rename note rides along with the body fail
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
        // are ad-dependent (no ad in F4-9); A5, C1 & C5 are pinned NotAssessedV1 in the rubric
        // (C5 sentence-level sv/en mixing joined in #488); D2/D3/D4/D5/D7/D10 & E1–E8 are
        // layout/font signals the deterministic parse cannot see. Every one MUST report
        // NotAssessed for the DEFAULT (layout-less) test CV. B5 LEFT this set in Fas 4b PR-6a
        // (#655, geometry-free bullet-marker consistency). B2 & D9 LEFT it in Fas 4b PR-6b
        // (#655): they are now assessable from ICvLayoutAnalyzer geometry — they verdict
        // NotAssessed ONLY when the CV lacks layout metrics (which the default Resume() has),
        // but are no longer ALWAYS-NotAssessed criteria, so they are covered by their own
        // B2PageCountRuleTests / D9FileSizeRuleTests. (E2 was already excluded here — it is
        // VisualOnly, absent from an Ats review.)
        var data = new TheoryData<string>();
        foreach (var id in new[]
        {
            "A3", "A5", "C1", "C5",
            "D2", "D3", "D4", "D5", "D7", "D8", "D10",
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

    [Fact]
    public async Task ReviewAsync_ShouldCarryAssetCivicReasonForC5_WhenPinnedNotAssessed()
    {
        // Parity A5/C1 (#488): the engine must surface C5's asset-authored civic reason — not the
        // code fallback, not a fabricated claim. #488's whole point is that C5 stops asserting a
        // property it never checks, so pin the exact user-facing wording.
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var c5 = Verdict(result, "C5");
        c5.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        c5.NotAssessedReason.ShouldBe(
            "Vi kontrollerar inte språkblandning mening för mening i den här versionen.");
    }

    [Fact]
    public async Task ReviewAsync_ShouldTallyC5UnderLanguageAsNotAssessed_NeverPass_WhenCalled()
    {
        // #488 band consequence made explicit: C5 is High-weight and previously fed a fabricated
        // Pass into the Språk band. Now NotAssessed it must sit in the Language category and be
        // counted as NotAssessed — so it neither lifts the numerator nor enters the denominator
        // (BuildCategories excludes NotAssessed). Combined with ShouldExposeVerdictCountsPerCategory
        // (category counts == verdict tally) this proves C5 lands in Language.NotAssessedCount.
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var c5 = Verdict(result, "C5");
        c5.Category.ShouldBe(RubricCategory.Language);
        c5.Verdict.ShouldBe(CriterionVerdict.NotAssessed);

        result.Categories.Single(c => c.Category == RubricCategory.Language)
            .NotAssessedCount.ShouldBeGreaterThan(0);
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
    // never throws. The real v2.0.0 asset always authors the field (Test G part 2 guards
    // that), so this fallback is only reachable through an older (N-1) provider — exactly
    // the seam this test drives.
    [Fact]
    public async Task ReviewAsync_ShouldUseCivicFallback_WhenNotAssessedReasonIsNull()
    {
        var engine = new CvReviewEngine(
            FakeRubricProviderWithNullReasonOnA5(),
            RealClicheLexicon(),
            RealVerbMapper(),
            Analyzer(),
            AllCorrectSpellChecker(),
            RealAllowlist());

        var result = await engine.ReviewAsync(
            CvReviewContext.FromParsed(Resume()), RenderProfile.Ats, TestContext.Current.CancellationToken);

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

    // ===============================================================
    // 12b. A4 gap detection — running max end date, not the immediately
    //      previous role; incomplete date coverage → honest NotAssessed (#493)
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldPassA4_WhenParallelRolesOverlapWithoutARealGap()
    {
        // #493: a long role (2010–2020) covers the span a shorter PARALLEL role (2012–2013) sits
        // inside, then a later role (2021–nuvarande) resumes ~1 month after. Comparing each role to
        // the immediately-previous one by start-order fabricated an 85-month gap (2013 → 2021);
        // tracking the RUNNING MAX end date (coverage reaches 2020) shows there is no real gap.
        var resume = Resume(experience:
        [
            Experience(period: "2010 – 2020", rawText: "Verksamhetschef 2010 – 2020"),
            Experience(period: "2012 – 2013", rawText: "Styrelseledamot (parallellt) 2012 – 2013"),
            Experience(period: "2021 – nuvarande", rawText: "Konsult 2021 – nuvarande"),
        ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "A4").Verdict.ShouldBe(CriterionVerdict.Pass,
            "En parallell roll inuti en längre roll ska inte fabricera en tidslucka (#493).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnA4_WhenThereIsARealUnexplainedGapBetweenFullyDatedRoles()
    {
        // A genuine > 6-month gap between fully-dated roles (2015 → 2018) is still flagged, cited
        // from the running max end so the reported span is correct.
        var resume = Resume(experience:
        [
            Experience(period: "2010 – 2015", rawText: "Utvecklare 2010 – 2015"),
            Experience(period: "2018 – 2020", rawText: "Utvecklare 2018 – 2020"),
        ]);

        var result = await ReviewAsync(resume);

        var a4 = Verdict(result, "A4");
        a4.Verdict.ShouldBe(CriterionVerdict.Warn);
        a4.Evidence.OfType<StructuralEvidence>().ShouldHaveSingleItem()
            .Observation.ShouldContain("2015");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotAssessA4_WhenAnApparentGapCoexistsWithAnUnparseablePeriod()
    {
        // #493 part 2: an unparseable period is silently dropped, so an apparent gap between two
        // dated roles could actually be filled by the undated role. With incomplete date coverage
        // A4 must not fabricate a Warn — it reports NotAssessed honestly.
        var resume = Resume(experience:
        [
            Experience(period: "2010 – 2015", rawText: "Utvecklare 2010 – 2015"),
            Experience(period: "2021 – 2022", rawText: "Utvecklare 2021 – 2022"),
            Experience(period: "en period däremellan", rawText: "Föräldraledig en period däremellan"),
        ]);

        var result = await ReviewAsync(resume);

        var a4 = Verdict(result, "A4");
        a4.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            "En skenbar lucka mellan daterade roller kan fyllas av en odaterad roll → NotAssessed (#493).");
        a4.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA4_WhenAnOngoingRoleInTheMiddleCoversLaterRoles()
    {
        // An ongoing role (End = DateOnly.MaxValue) sorted BEFORE later dated roles must stretch the
        // running max end to "now", so no later role opens a gap behind it. Against the immediately-
        // previous shorter role (2012–2013) a gap would be fabricated at 2020 (#493).
        var resume = Resume(experience:
        [
            Experience(period: "2010 – nuvarande", rawText: "Verksamhetschef 2010 – nuvarande"),
            Experience(period: "2012 – 2013", rawText: "Styrelseledamot 2012 – 2013"),
            Experience(period: "2020 – 2021", rawText: "Konsult 2020 – 2021"),
        ]);

        Verdict(await ReviewAsync(resume), "A4").Verdict.ShouldBe(CriterionVerdict.Pass,
            "En pågående roll mitt i sekvensen täcker alla senare roller (#493).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldCiteTheRunningMaxEnd_WhenAnOverlapPrecedesARealGap()
    {
        // A long role (2010–2020) overlaps a shorter parallel role (2012–2013); a real gap follows
        // to 2022. The cited span must read the running max end (2020), NOT the immediately-previous
        // shorter role's end (2013) — else the reported gap span is wrong (#493).
        var resume = Resume(experience:
        [
            Experience(period: "2010 – 2020", rawText: "Verksamhetschef 2010 – 2020"),
            Experience(period: "2012 – 2013", rawText: "Styrelseledamot 2012 – 2013"),
            Experience(period: "2022 – 2023", rawText: "Konsult 2022 – 2023"),
        ]);

        var a4 = Verdict(await ReviewAsync(resume), "A4");
        a4.Verdict.ShouldBe(CriterionVerdict.Warn);
        var observation = a4.Evidence.OfType<StructuralEvidence>().ShouldHaveSingleItem().Observation;
        observation.ShouldContain("2020-12");   // the running max end
        observation.ShouldNotContain("2013");   // NOT the immediately-previous shorter role's end
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA4_WhenTimelineIsGapFreeDespiteAnUnparseablePeriod()
    {
        // The NotAssessed qualifier must fire ONLY when an apparent gap exists; a contiguous
        // timeline with one extra undated role still Passes (an undated role can only add coverage).
        var resume = Resume(experience:
        [
            Experience(period: "2010 – 2015", rawText: "Utvecklare 2010 – 2015"),
            Experience(period: "2015 – 2020", rawText: "Utvecklare 2015 – 2020"),
            Experience(period: "en period däremellan", rawText: "Föräldraledig en period däremellan"),
        ]);

        Verdict(await ReviewAsync(resume), "A4").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Gap-fri tidslinje med en odaterad roll ska passera, inte bli NotAssessed (#493).");
    }

    [Theory]
    // Gap in whole months from the running max end to the next start; > 6 = Warn, exactly 6 = Pass (strict).
    [InlineData("06/2019 – 01/2020", "07/2020 – 12/2020", CriterionVerdict.Pass)] // 2020-01→2020-07 = 6 mån
    [InlineData("06/2019 – 01/2020", "08/2020 – 12/2020", CriterionVerdict.Warn)] // 2020-01→2020-08 = 7 mån
    [InlineData("01/2018 – 10/2020", "04/2021 – 12/2021", CriterionVerdict.Pass)] // crosses year: 6 mån
    [InlineData("01/2018 – 10/2020", "05/2021 – 12/2021", CriterionVerdict.Warn)] // crosses year: 7 mån
    public async Task ReviewAsync_ShouldTreatSixMonthsAsTheStrictGapBoundary_ForA4(
        string first, string second, CriterionVerdict expected)
    {
        var resume = Resume(experience:
        [
            Experience(period: first, rawText: $"Roll A {first}"),
            Experience(period: second, rawText: $"Roll B {second}"),
        ]);

        Verdict(await ReviewAsync(resume), "A4").Verdict.ShouldBe(expected);
    }

    [Fact]
    public async Task ReviewAsync_ShouldReportEveryGap_WhenThreeUnexplainedGapsExist()
    {
        // The running max end advances correctly past each gap → all three gaps are cited (§5
        // explainability: the user sees every gap, not just the first).
        var resume = Resume(experience:
        [
            Experience(period: "2000 – 2001", rawText: "Roll A 2000 – 2001"),
            Experience(period: "2003 – 2004", rawText: "Roll B 2003 – 2004"),
            Experience(period: "2006 – 2007", rawText: "Roll C 2006 – 2007"),
            Experience(period: "2009 – 2010", rawText: "Roll D 2009 – 2010"),
        ]);

        var observation = Verdict(await ReviewAsync(resume), "A4")
            .Evidence.OfType<StructuralEvidence>().ShouldHaveSingleItem().Observation;
        observation.ShouldContain("2001-12");
        observation.ShouldContain("2003-01");
        observation.ShouldContain("2004-12");
        observation.ShouldContain("2006-01");
        observation.ShouldContain("2007-12");
        observation.ShouldContain("2009-01");
    }

    [Fact]
    public async Task ReviewAsync_ShouldPassA4_WhenThereIsOnlyOneDatedRole()
    {
        // The single-dated-role branch: no pair to compare → no gap → Pass (belt-and-suspenders
        // coverage of an otherwise-untested A4 branch).
        var resume = Resume(experience:
        [
            Experience(period: "2019 – 2021", rawText: "Utvecklare 2019 – 2021"),
        ]);

        Verdict(await ReviewAsync(resume), "A4").Verdict.ShouldBe(CriterionVerdict.Pass);
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
            experience: [Experience(bullets: ["Ansvarade för diverse uppgifter utan resultat."])],
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
            experience: [Experience(bullets: ["Var ansvarig för diverse uppgifter."])]);

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
        // A Swedish CV (DetectedLanguage.Sv) → the engine routes the assessed NLP-tier criteria
        // (C2/C3/C4) through TextLanguage.Swedish and assesses them. C5 (sentence-level sv/en
        // mixing) is NotAssessedV1 (#488) and C1 is pinned NotAssessedV1 — neither is claimed here.
        var resume = Resume(detectedLanguage: ResumeLanguage.Sv);

        var result = await ReviewAsync(resume);

        Verdict(result, "C2").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed);
        Verdict(result, "C3").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    [Fact]
    public async Task ReviewAsync_ShouldReportC5AsNotAssessed_BecauseSentenceLevelMixingIsNotChecked()
    {
        // #488: C5 Språkkonsistens previously returned an UNCONDITIONAL Pass with a fabricated
        // citation — asserting a property the engine never checks (the F4-8 detector only picks
        // a DOMINANT document language; a 50/50 sv/en CV still gets a dominant pick). Honest state
        // = NotAssessed with no fabricated evidence (parity A5/C1), never a mis-reported Pass
        // (CLAUDE.md §5/§12 honesty contract). The Språk-band consequence is asserted separately
        // in ReviewAsync_ShouldTallyC5UnderLanguageAsNotAssessed_NeverPass.
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var c5 = Verdict(result, "C5");
        c5.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            "C5 får aldrig fabricera ett Pass för språkkonsistens motorn inte kontrollerar (#488).");
        c5.Evidence.ShouldBeEmpty();
        c5.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
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
                    bullets: ["Led a team of 8 and increased conversion by 23% in 2024."]),
            ]);

        var result = await ReviewAsync(resume);

        Verdict(result, "C3").Verdict.ShouldNotBe(CriterionVerdict.NotAssessed,
            "En engelsk CV ska bedömas via TextLanguage.English, inte kasta NotSupported.");
    }

    // ===============================================================
    // 15b. C3 Aktivt språk — deponens exception + proper-noun exclusion
    //      (#492) and a rubric-reconciled passive-ratio (#489)
    // ===============================================================

    [Theory]
    [InlineData("Jag lyckades öka försäljningen med 30 procent. Jag trivdes i en ledande roll.")]
    [InlineData("Under projektet hoppades jag på mer ansvar och andades ut när det gick vägen.")]
    public async Task ReviewAsync_ShouldPassC3_WhenSwedishProseUsesDeponentVerbs(string profile)
    {
        // #492: deponens ("lyckades", "trivdes", "hoppades", "andades") are s-form but ACTIVE in
        // meaning — exactly the achievement language A1 rewards. Pre-fix two such forms tripped the
        // absolute count-2 Warn, so the engine contradicted itself. They are now excluded → Pass.
        Verdict(await ReviewAsync(Resume(profile: profile, experience: [])), "C3").Verdict
            .ShouldBe(CriterionVerdict.Pass,
                "Deponens är aktiva i betydelse och ska inte flaggas som passiv (#492).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldNotFlagCapitalInitialProperNounsAsPassive()
    {
        // #492: "Mercedes" ends in -des and matched the s-passive shape. A capital-initial token is a
        // proper noun, never a verb mid-sentence → excluded. Two of them no longer trip a Warn.
        var resume = Resume(
            profile: "Jag sålde en Mercedes till kund. Jag levererade en annan Mercedes samma år.",
            experience: []);

        Verdict(await ReviewAsync(resume), "C3").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Egennamn som 'Mercedes' är inte passiv form (#492).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailC3WithTextSpan_WhenPassiveRatioExceedsThirtyPercent()
    {
        // #489 ratio reconcile: rubric atsFailSignal ">30 % passiv form". Two genuine s-passives in
        // five sentences (0.4) FAIL — pre-fix an absolute count could never reach the rubric's Fail.
        var resume = Resume(
            profile: "Rapporten hanterades av teamet. Beslutet godkändes av chefen. "
                + "Jag ledde projektet. Jag ökade försäljningen. Jag byggde plattformen.",
            experience: []);

        var c3 = Verdict(await ReviewAsync(resume), "C3");
        c3.Verdict.ShouldBe(CriterionVerdict.Fail, "0,4 passiv/mening > 30 % → C3 Fail (#489).");
        // §5: the Fail cites the FIRST genuine passive span ("hanterades"), never an arbitrary offset.
        c3.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote.ShouldBe("hanterades");
    }

    [Fact]
    public async Task ReviewAsync_ShouldWarnC3AndCiteTheGenuinePassive_WhenASinglePassiveIsBelowTheThreshold()
    {
        // A single genuine passive below the 30 % ratio is a Warn (not a Fail, not a Pass) — pre-fix
        // one passive was under the count-2 gate and silently PASSED. The Warn cites the passive.
        var resume = Resume(
            profile: "Rapporten hanterades av teamet. Jag ledde projektet. "
                + "Jag ökade försäljningen. Jag byggde plattformen.",
            experience: []);

        var c3 = Verdict(await ReviewAsync(resume), "C3");
        c3.Verdict.ShouldBe(CriterionVerdict.Warn, "En äkta passiv under 30 %-gränsen → C3 Warn (#489).");
        c3.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote.ShouldBe("hanterades",
            "Warn ska citera den äkta passiven, inte ett godtyckligt span (§5).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldCountOnlyGenuinePassives_WhenDeponensAndProperNounsCoexist()
    {
        // The core #492 claim, pinned: when a deponens ("lyckades") AND a proper noun ("Mercedes")
        // AND one GENUINE passive ("hanterades") coexist, ONLY the genuine one is counted and cited.
        // A regression that dropped ALL -ades/-des matches (incl. the genuine one) would still pass
        // the isolated deponens/proper-noun tests, but fails HERE (the genuine passive must survive).
        var resume = Resume(
            profile: "Jag lyckades sälja en Mercedes. Rapporten hanterades av teamet. "
                + "Jag ledde projektet. Jag byggde plattformen.",
            experience: []);

        var c3 = Verdict(await ReviewAsync(resume), "C3");
        // 1 genuine passive ("hanterades") in 4 sentences = 0.25 → Warn (deponens + Mercedes excluded).
        c3.Verdict.ShouldBe(CriterionVerdict.Warn,
            "Bara den äkta passiven räknas; deponens och egennamn exkluderas (#492).");
        c3.Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem().Span.Quote.ShouldBe("hanterades",
            "Citatet ska peka på den äkta passiven, inte deponensen/egennamnet (#492).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldFailC3_WhenEnglishProseLeansOnBePassive()
    {
        // The English be-passive arm is ratio-scored too: three be-passives in three sentences (1.0)
        // → Fail. Deponens/proper-noun filters are Swedish-only and do not apply here.
        var resume = Resume(
            detectedLanguage: ResumeLanguage.En,
            profile: "The system was designed by me. The report was written by the team. "
                + "The plan was approved by the board.",
            experience: []);

        Verdict(await ReviewAsync(resume), "C3").Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    [Fact]
    public async Task ReviewAsync_C3RatioShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // Golden drift-guard (#489, parity A7): derive the C3 Fail ratio from the versioned rubric
        // prose (atsFailSignal ">30 % passiv form") rather than a hardcoded constant, so code that
        // drifts from the rubric fails CI.
        var c3 = RealRubric().Criteria.Single(c => c.Id == "C3");
        var failPercent = FirstInt(c3.AtsFailSignal!);   // 30
        var failRatio = failPercent / 100.0;             // 0.30

        // 10 sentences: passivesAbove/10 strictly exceeds the ratio; passivesBelow/10 sits at it.
        var passivesAbove = (int)Math.Floor(failRatio * 10) + 1;   // 4 → 0.40 > 0.30
        var passivesBelow = (int)Math.Floor(failRatio * 10);       // 3 → 0.30, not > 0.30

        Verdict(await ReviewAsync(Resume(profile: PassiveProse(passivesAbove, 10), experience: [])), "C3")
            .Verdict.ShouldBe(CriterionVerdict.Fail, $"{passivesAbove}/10 > {failRatio:0.0#} → Fail.");
        Verdict(await ReviewAsync(Resume(profile: PassiveProse(passivesBelow, 10), experience: [])), "C3")
            .Verdict.ShouldNotBe(CriterionVerdict.Fail, $"{passivesBelow}/10 = {failRatio:0.0#} → inte Fail.");

        // Builds `total` sentences of which `passive` are genuine s-passives and the rest active.
        static string PassiveProse(int passive, int total)
        {
            var sentences = new List<string>();
            for (var i = 0; i < total; i++)
            {
                sentences.Add(i < passive ? "Rapporten hanterades av teamet" : "Jag ledde arbetet");
            }

            return string.Join(". ", sentences) + ".";
        }
    }

    // ===============================================================
    // 16. C4 Konsekvent perspektiv — third-person pronouns only, NOT the
    //     Swedish demonstratives "denna/denne" (#491)
    // ===============================================================

    [Theory]
    [InlineData("I denna roll ansvarade jag för budget, personal och rekrytering.")]
    [InlineData("Under denna period drev jag flera parallella projekt.")]
    [InlineData("Denne kund var min största under 2024.")]
    public async Task ReviewAsync_ShouldPassC4_WhenProseUsesTheDemonstrativeDennaDenne(string profile)
    {
        // #491: "denna/denne" are DEMONSTRATIVES, not third-person narration — "i denna roll
        // ansvarade jag …" is ordinary first-person Swedish CV prose. C4 must not raise a false
        // "tredje person" Warn on them (they were wrongly in the pronoun set).
        var result = await ReviewAsync(Resume(profile: profile));

        Verdict(result, "C4").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Demonstrativa 'denna/denne' är inte tredje-persons-narration → C4 Pass (#491).");
    }

    [Theory]
    [InlineData("han")]
    [InlineData("hon")]
    [InlineData("hen")]
    [InlineData("he")]
    [InlineData("she")]
    public async Task ReviewAsync_ShouldWarnC4_WhenProseUsesARealThirdPersonPronoun(string pronoun)
    {
        // The genuine third-person narration case is retained: a real personal pronoun ("Anna är
        // en driven … han ledde …") is still flagged. Pinned so dropping the demonstratives does
        // not weaken the real signal.
        var result = await ReviewAsync(Resume(profile: $"Erfaren utvecklare, {pronoun} ledde teamet under 2024."));

        var c4 = Verdict(result, "C4");
        c4.Verdict.ShouldBe(CriterionVerdict.Warn,
            $"Ett riktigt tredje-persons-pronomen ('{pronoun}') ska fortfarande flaggas av C4.");
        c4.Evidence.ShouldContain(e => e is TextSpanEvidence);
    }

    [Theory]
    [InlineData("Johan Svensson ledde teamet under 2024.")]           // "han" inside Johan
    [InlineData("Johanna ansvarade för hela handeln på Acme.")]       // "han" inside Johanna/handeln
    [InlineData("Levererade honung och honnörsavtal till kunden.")]   // "hon" inside honung/honnör
    [InlineData("The shelf here shone when polished, hence pristine.")] // he/she/hen as substrings
    public async Task ReviewAsync_ShouldPassC4_WhenAWordMerelyContainsAPronounAsASubstring(string profile)
    {
        // Non-regression on the word boundary (\b): a pronoun that is only a SUBSTRING of a longer
        // word (Johan, handeln, honung; the/shelf/when/hence) is not third person → C4 stays Pass.
        // Pins the anti-false-positive contract #491 exists to protect — a future weakening of the
        // boundary would re-introduce a false "tredje person" Warn on every "e-handel" CV.
        var result = await ReviewAsync(Resume(profile: profile));

        Verdict(result, "C4").Verdict.ShouldBe(CriterionVerdict.Pass,
            "Ett pronomen som substräng i ett längre ord är inte tredje person → C4 Pass (#491).");
    }

    [Theory]
    [InlineData("Han")]
    [InlineData("Hon")]
    [InlineData("HON")]
    [InlineData("She")]
    public async Task ReviewAsync_ShouldWarnC4_WhenARealThirdPersonPronounIsCapitalised(string pronoun)
    {
        // Pins RegexOptions.IgnoreCase: the most common real third-person form is sentence-initial
        // ("Anna är driven. Hon ledde teamet.") or upper-case. Without this a removed IgnoreCase
        // would silently pass the canonical form.
        var result = await ReviewAsync(Resume(profile: $"{pronoun} ledde teamet under 2024."));

        Verdict(result, "C4").Verdict.ShouldBe(CriterionVerdict.Warn,
            $"Ett versalt tredje-persons-pronomen ('{pronoun}') ska flaggas av C4 (IgnoreCase).");
    }

    [Fact]
    public async Task ReviewAsync_ShouldCiteTheOffendingPronounSpan_WhenC4Warns()
    {
        // Invariant 2 (§5): C4's TextSpan cites the PRONOUN itself (case-preserved), not an
        // arbitrary offset — the verdict is grounded in the exact evidence it flags.
        var result = await ReviewAsync(
            Resume(profile: "Erfaren utvecklare. Hon ledde teamet under 2024."));

        var span = Verdict(result, "C4").Evidence.OfType<TextSpanEvidence>().ShouldHaveSingleItem();
        span.Span.Quote.ShouldBe("Hon");
    }

    // ===============================================================
    // 17. A2/A6/A4 prose↔data golden drift-guards (rubric v1.2, PR-5 CTO-bind D1)
    //     — the numeric thresholds relocated to RubricCriterion.Thresholds must
    //     still equal the numbers their user-facing prose signals carry (DRY: one
    //     knowledge piece, two representations that must agree), AND the engine
    //     must behave at the DERIVED boundary. Parity with the A1/A7/A8/C3 guards.
    //     These are among the exact thresholds this PR moves, so guarding them IS
    //     the move's Definition of Done — never a fresh hardcoded expectation.
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_A2ThresholdsShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // A2 PASS "≥80 %" → thresholds.passRatio 0.80; FAIL "<50 %" → thresholds.failRatio 0.50.
        // Both are the "%"-bearing numbers, read with PercentInSignal (parity A1's helper).
        var a2 = RealRubric().Criteria.Single(c => c.Id == "A2");
        var passPercent = PercentInSignal(a2.AtsPassSignal!);   // 80
        var failPercent = PercentInSignal(a2.AtsFailSignal!);   // 50

        a2.RequiredThreshold(RubricThresholdKeys.PassRatio).ShouldBe(passPercent / 100.0, 1e-9);
        a2.RequiredThreshold(RubricThresholdKeys.FailRatio).ShouldBe(failPercent / 100.0, 1e-9);

        // Behaviour at the derived boundary: strong-opener share ≥ passRatio → Pass; below → not
        // Pass; strictly below failRatio → Fail; exactly at failRatio → Warn (strict <, not Fail).
        VerdictOf(await ReviewAsync(A2Bullets(strong: 8, total: 10)), "A2")
            .ShouldBe(CriterionVerdict.Pass, "8/10 starka (0,80 ≥ passRatio) → Pass.");
        VerdictOf(await ReviewAsync(A2Bullets(strong: 7, total: 10)), "A2")
            .ShouldNotBe(CriterionVerdict.Pass, "7/10 (0,70 < passRatio) → inte Pass.");
        VerdictOf(await ReviewAsync(A2Bullets(strong: 4, total: 10)), "A2")
            .ShouldBe(CriterionVerdict.Fail, "4/10 (0,40 < failRatio) → Fail.");
        var a2AtFail = Verdict(await ReviewAsync(A2Bullets(strong: 5, total: 10)), "A2").Verdict;
        a2AtFail.ShouldBe(CriterionVerdict.Warn, "5/10 (exakt 0,50, inte < failRatio) → Warn.");
        a2AtFail.ShouldNotBe(CriterionVerdict.Fail);

        // `total` bullets; `strong` open with a strong mapping verb, the rest with a NEUTRAL
        // (non-strong, non-weak) opener so the non-strong share is honest and never a weak-verb Fail.
        static ParsedResume A2Bullets(int strong, int total)
        {
            var strongVerb = RealVerbMapper().GetVerbMapping()
                .StrongVerbGroups.SelectMany(g => g.Verbs).First();
            var bullets = new List<string>();
            for (var i = 0; i < total; i++)
            {
                bullets.Add(i < strong
                    ? $"{Capitalize(strongVerb)} teamet mot tydliga mål"
                    : "Övriga uppgifter enligt plan");
            }

            return Resume(experience: [Experience(bullets: [.. bullets])]);
        }
    }

    [Fact]
    public async Task ReviewAsync_A6ThresholdsShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // A6 PASS "≥70 %" → thresholds.passRatio 0.70; FAIL ">50 % generiska" → thresholds.failRatio
        // 0.50. The FAIL prose numeral is 50; the rule fails when the CONCRETE ratio < 0.50 (i.e.
        // more than 50 % generic). The guard pins the DATA against the prose NUMERALS, so a copy
        // edit to the signal that changes the number can never silently diverge from the threshold.
        var a6 = RealRubric().Criteria.Single(c => c.Id == "A6");
        var passPercent = PercentInSignal(a6.AtsPassSignal!);   // 70
        var failPercent = PercentInSignal(a6.AtsFailSignal!);   // 50

        a6.RequiredThreshold(RubricThresholdKeys.PassRatio).ShouldBe(passPercent / 100.0, 1e-9);
        a6.RequiredThreshold(RubricThresholdKeys.FailRatio).ShouldBe(failPercent / 100.0, 1e-9);

        // Behaviour at the derived boundary: concrete share ≥ passRatio → Pass; below → not Pass;
        // strictly below failRatio → Fail; exactly at failRatio → Warn (strict <, not Fail).
        VerdictOf(await ReviewAsync(A6Bullets(concrete: 7, total: 10)), "A6")
            .ShouldBe(CriterionVerdict.Pass, "7/10 konkreta (0,70 ≥ passRatio) → Pass.");
        VerdictOf(await ReviewAsync(A6Bullets(concrete: 6, total: 10)), "A6")
            .ShouldNotBe(CriterionVerdict.Pass, "6/10 (0,60 < passRatio) → inte Pass.");
        VerdictOf(await ReviewAsync(A6Bullets(concrete: 4, total: 10)), "A6")
            .ShouldBe(CriterionVerdict.Fail, "4/10 konkreta (0,40 < failRatio) → Fail.");
        var a6AtFail = Verdict(await ReviewAsync(A6Bullets(concrete: 5, total: 10)), "A6").Verdict;
        a6AtFail.ShouldBe(CriterionVerdict.Warn, "5/10 konkreta (exakt 0,50, inte < failRatio) → Warn.");
        a6AtFail.ShouldNotBe(CriterionVerdict.Fail);

        // `total` bullets; `concrete` carry a measurable metric (a concrete artefact), the rest are
        // generic (no digit, no capitalised named system). Dates are irrelevant here.
        static ParsedResume A6Bullets(int concrete, int total)
        {
            var bullets = new List<string>();
            for (var i = 0; i < total; i++)
            {
                bullets.Add(i < concrete
                    ? "Ökade resultatet med 20 procent"
                    : "ansvarade för dagliga arbetsuppgifter");
            }

            return Resume(experience: [Experience(bullets: [.. bullets])]);
        }
    }

    [Fact]
    public async Task ReviewAsync_A4MaxGapMonthsShouldMatchTheRubricProse_GoldenDriftGuard()
    {
        // A4 maxGapMonths is derived from the PASS signal "Inga oförklarade gaps >6 mån" (FirstInt).
        // The FAIL signal ("≥1 oförklarat gap >6 mån ...") LEADS with "1", so FirstInt on it would
        // wrongly read 1 — the guard reads the PASS signal, exactly as the CTO record directs.
        var a4 = RealRubric().Criteria.Single(c => c.Id == "A4");
        var gapMonths = FirstInt(a4.AtsPassSignal!);   // 6

        ((int)a4.RequiredThreshold(RubricThresholdKeys.MaxGapMonths)).ShouldBe(gapMonths,
            "A4 maxGapMonths-DATA måste vara samma tal som prosans \">6 mån\".");

        // Boundary behaviour at the derived value: a gap of exactly `gapMonths` months is NOT > the
        // threshold → Pass; one month more → Warn (strict >). Parity with the existing A4 Theory,
        // but with the boundary DERIVED from the data rather than hardcoded.
        VerdictOf(await ReviewAsync(TwoRolesWithGap(gapMonths)), "A4")
            .ShouldBe(CriterionVerdict.Pass, $"exakt {gapMonths} mån är inte > {gapMonths} → Pass.");
        VerdictOf(await ReviewAsync(TwoRolesWithGap(gapMonths + 1)), "A4")
            .ShouldBe(CriterionVerdict.Warn, $"{gapMonths + 1} mån > {gapMonths} → Warn.");

        // Role A ends 2020-01; role B starts `gapMonths` months later (both parseable MM/YYYY, no
        // unparseable period so A4 is assessed). The gap in whole months from the running max end
        // is the offset from January 2020.
        static ParsedResume TwoRolesWithGap(int gapMonths)
        {
            var startMonth = 1 + gapMonths;   // months after 2020-01; stays within the year for 6/7
            var second = $"{startMonth:00}/2020 – 12/2020";
            return Resume(experience:
            [
                Experience(period: "06/2019 – 01/2020", rawText: "Roll A 06/2019 – 01/2020"),
                Experience(period: second, rawText: $"Roll B {second}"),
            ]);
        }
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
