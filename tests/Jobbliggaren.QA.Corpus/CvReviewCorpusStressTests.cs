using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.QA.Corpus.Generation;
using Jobbliggaren.QA.Corpus.Harness;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Fas 4 STEG C, PR 3 — the reviewer frontend (CTO Fork 1 = 1D). Runs the seeded CV corpus
/// through the REAL <c>ICvReviewEngine</c> (F4-9) — NOT mocked — wired to the REAL knowledge
/// bank (rubric/cliché/verb loaders) and the REAL Swedish Snowball analyzer (more faithful than
/// the unit tests' whitespace stub). Pure in-memory: no DB, no Testcontainer (the reviewer
/// consumes an in-memory <c>ParsedResume</c>; the field-encryption pipeline is the handler's
/// concern, not the engine's).
///
/// <para>GATING (CTO Fork 4/5 — the only assertions; per-criterion hit-rate is observe-only and
/// reported in PR 4):</para>
/// <list type="number">
/// <item><b>Crash-safety = 100%</b> across every stratum, for BOTH render profiles (5C).</item>
/// <item><b>Valid envelope</b> for every CV: a verdict per in-profile criterion, version
/// stamped, AssessedCount honest, and Invariant 2 holds — every assessed verdict cites ≥1
/// evidence.</item>
/// <item><b>PII-safety (ADR 0074 Invariant 1):</b> a fake-personnummer CV makes B4 fire, and
/// NO cited evidence anywhere echoes the raw personnummer (B4 cites the count structurally,
/// never the value).</item>
/// </list>
/// </summary>
public class CvReviewCorpusStressTests(ITestOutputHelper output)
{
    // The reviewer does not derive SSYK, so CV titles need not be real taxonomy labels — a small
    // synthetic ground-truth keeps this frontend pure in-memory (CTO Fork 1 — reviewer = in-memory).
    private static readonly IReadOnlyList<OccupationGroundTruth> SyntheticGroundTruth =
    [
        new("Mjukvaruutvecklare", "synthetic"),
        new("Sjuksköterska", "synthetic"),
        new("Snickare", "synthetic"),
        new("Ekonomiassistent", "synthetic"),
        new("Lärare", "synthetic"),
    ];

    // Fas 4b 8b.4b (ADR 0108): the REAL lexicon + conventions asset (cross-asset-pinned in the
    // provider's ctor), so the corpus stresses B1's order half against the shipped pair.
    private static readonly Lazy<CvParsingLexiconData> LazyLexicon = new(CvParsingLexiconLoader.Load);

    private static CvReviewEngine NewEngine() =>
        new(new RubricProvider(), new ClicheLexicon(), new VerbMapper(),
            new LocalTextAnalyzer(new SnowballStemmer()),
            // Fas 4b PR-6a (#655): the REAL Hunspell checker + REAL committed allowlist so C7
            // is exercised end-to-end against the shipped DSSO/en_US dictionaries (more faithful
            // than a stub, parity the real knowledge bank + Snowball analyzer above).
            new HunspellSpellChecker(), new SpellingAllowlistProvider(),
            new CvConventionsProvider(new CvParsingLexiconProvider(LazyLexicon.Value)),
            LazyLexicon.Value);

    private static IReadOnlyList<GeneratedCvCase> Corpus() =>
        new CorpusGenerator().GenerateCvCorpus(SyntheticGroundTruth);

    // ===============================================================
    // GATE 1 — Crash-safety MUST be 100% across every stratum, both profiles (CTO Fork 5 = 5C)
    // ===============================================================

    [Fact]
    public async Task CvReviewCorpus_IsCrashSafe_AcrossEveryStratum_BothProfiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var corpus = Corpus();
        corpus.Count.ShouldBeGreaterThan(200, "the stress corpus must be non-trivial.");

        var engine = NewEngine();
        var outcomes = await CrashSweep.RunAsync(
            corpus,
            c => c.Label,
            c => c.Stratum,
            async (c, t) =>
            {
                _ = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), RenderProfile.Ats, t);
                _ = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), RenderProfile.Visual, t);
            },
            ct);

        var crashes = outcomes.Crashes();
        crashes.ShouldBeEmpty(
            "KRASCH-SÄKERHET BRUTEN — granskaren kastade på: " +
            string.Join(", ", crashes.Select(x => $"{x.Label} ({x.ExceptionType})")));

        foreach (var stratumGroup in outcomes.GroupBy(o => o.Stratum))
            stratumGroup.Count(o => o.Threw).ShouldBe(0,
                $"strata {stratumGroup.Key} ska vara 100% krasch-fri.");
    }

    // ===============================================================
    // GATE 2 — Every review yields a valid envelope (Invariant 2 holds across the corpus)
    // ===============================================================

    [Fact]
    public async Task EveryReview_ProducesAValidEnvelope_WithCitedEvidenceForAssessedVerdicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();

        foreach (var c in Corpus())
        {
            var result = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), RenderProfile.Ats, ct);

            result.Profile.ShouldBe(RenderProfile.Ats, $"{c.Label}: the result echoes the requested profile.");
            result.Verdicts.ShouldNotBeEmpty($"{c.Label}: at least one criterion per profile.");
            result.TotalCount.ShouldBe(result.Verdicts.Count, $"{c.Label}: TotalCount = verdict count.");
            result.AssessedCount.ShouldBe(
                result.Verdicts.Count(v => v.Verdict != CriterionVerdict.NotAssessed),
                $"{c.Label}: AssessedCount excludes NotAssessed.");

            // ADR 0074 Invariant 2: every assessed verdict cites ≥1 evidence; NotAssessed carries a reason.
            foreach (var v in result.Verdicts)
            {
                if (v.Verdict == CriterionVerdict.NotAssessed)
                    v.NotAssessedReason.ShouldNotBeNullOrWhiteSpace($"{c.Label}/{v.CriterionId}: NotAssessed needs a reason.");
                else
                    v.Evidence.ShouldNotBeEmpty($"{c.Label}/{v.CriterionId}: assessed verdict must cite evidence (Inv. 2).");
            }
        }
    }

    // ===============================================================
    // GATE 3 — PII-safety (ADR 0074 Invariant 1): a fake-personnummer CV FAILS B4, and B4's
    // evidence stays count-only (StructuralEvidence — never the raw value).
    //
    // HISTORY: STEG C surfaced (as an observe-only finding) that NON-B4 criteria (A1/A2/A6/A8)
    // echoed a personnummer in their TextSpanEvidence when the user placed it inside the
    // profile/experience text those criteria cite. The PII-hardening STEG closed it (the review
    // engine now redacts pnr from ALL evidence quotes/notes via PersonnummerRedactor), so that
    // finding is now a GATED regression invariant — see
    // NoReviewEvidence_EchoesARawPersonnummer_AcrossTheWholeCorpus below (CTO Fork 4A: the
    // observe-only finding becomes a passing gate).
    // ===============================================================

    [Fact]
    public async Task FakePersonnummerCvs_FailB4_WithPiiSafeEvidence()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();
        var pnrCases = Corpus().Where(c => c.Stratum == CorpusStratum.FakePersonnummer).ToList();
        pnrCases.ShouldNotBeEmpty();

        foreach (var c in pnrCases)
        {
            var result = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), RenderProfile.Ats, ct);

            // B4 (personnummeravsnitt) is a critical criterion — a flagged pnr must NOT pass.
            var b4 = result.Verdicts.SingleOrDefault(v => v.CriterionId == "B4");
            b4.ShouldNotBeNull($"{c.Label}: B4 should be assessed in the ATS profile.");
            b4!.Verdict.ShouldBe(CriterionVerdict.Fail,
                $"{c.Label}: a CV with a personnummer must FAIL B4 (critical, ADR 0071 Decision 2).");

            // B4's evidence is the count-only structural channel — it must NEVER echo the raw value.
            foreach (var s in EvidenceStrings(b4))
                foreach (var fake in SwedishCorpusLexicon.FakePersonnummer)
                    s.Contains(fake, StringComparison.Ordinal).ShouldBeFalse(
                        $"{c.Label}: B4 evidence echoed a raw personnummer — ADR 0074 Invariant 1 violated.");
        }
    }

    [Fact]
    public async Task NoReviewEvidence_EchoesARawPersonnummer_AcrossTheWholeCorpus()
    {
        // The hardened invariant (CTO Fork 4A): across the WHOLE corpus + both profiles, NO cited
        // evidence (any criterion, Quote or Note) may echo a raw personnummer — the review engine
        // redacts them via PersonnummerRedactor before the result is assembled. This is the STEG C
        // finding turned into a passing regression gate.
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();

        foreach (var c in Corpus())
        {
            foreach (var profile in new[] { RenderProfile.Ats, RenderProfile.Visual })
            {
                var result = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), profile, ct);
                foreach (var s in result.Verdicts.SelectMany(EvidenceStrings))
                    foreach (var fake in SwedishCorpusLexicon.FakePersonnummer)
                        s.Contains(fake, StringComparison.Ordinal).ShouldBeFalse(
                            $"{c.Label}/{profile}: cited evidence echoed a raw personnummer — " +
                            "the engine's evidence-redaction regressed (ADR 0074 Inv. 1).");
            }
        }
    }

    // ===============================================================
    // Wiring anchor — the harness exercises the REAL engine + REAL rubric, not a no-op
    // ===============================================================

    [Fact]
    public async Task Reviewer_IsWiredToRealRubric_AndEvaluatesRules()
    {
        var ct = TestContext.Current.CancellationToken;
        // A strong, clean CV must produce assessed verdicts incl. at least one Pass — proving the
        // real rules ran against the real knowledge bank (not a stub returning NotAssessed).
        var clean = Corpus().First(c => c.Stratum == CorpusStratum.CleanExactTitle);

        var result = await NewEngine().ReviewAsync(CvReviewContext.FromParsed(clean.Cv), RenderProfile.Ats, ct);

        result.AssessedCount.ShouldBeGreaterThan(0, "a clean CV must have assessed criteria.");
        result.Verdicts.ShouldContain(v => v.Verdict == CriterionVerdict.Pass,
            "a strong, clean CV must pass at least one criterion (real rule logic ran).");
    }

    // ===============================================================
    // GATE (Fas 4b PR-6a, #655) — C7 + B5 verdict BOUNDS across the whole corpus with the REAL
    // Hunspell + REAL allowlist. C7 (spelling, WARN-posture) is Pass/Warn and NEVER Fail; B5
    // (formatting, geometry-free) is Warn/NotAssessed and NEVER Pass. The clean-stratum C7 Warn
    // RATE is OBSERVED and LOGGED (CTO Fork 6 — observe-only), never hard-capped: a spike would
    // signal corpus-vocabulary drift (a generator word bank / allowlist gap), not a rule bug.
    // ===============================================================

    [Fact]
    public async Task C7AndB5_HonourTheirVerdictBounds_AcrossTheWholeCorpus_BothProfiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();

        var cleanC7Total = 0;
        var cleanC7Warns = 0;

        foreach (var c in Corpus())
        {
            foreach (var profile in new[] { RenderProfile.Ats, RenderProfile.Visual })
            {
                var result = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), profile, ct);

                var c7 = result.Verdicts.SingleOrDefault(v => v.CriterionId == "C7");
                if (c7 is not null)
                {
                    c7.Verdict.ShouldNotBe(CriterionVerdict.Fail,
                        $"{c.Label}/{profile}: C7 är WARN-posture (maskinell stavning) — aldrig Fail.");

                    if (profile == RenderProfile.Ats && c.Stratum == CorpusStratum.CleanExactTitle)
                    {
                        cleanC7Total++;
                        if (c7.Verdict == CriterionVerdict.Warn) cleanC7Warns++;
                    }
                }

                var b5 = result.Verdicts.SingleOrDefault(v => v.CriterionId == "B5");
                b5.ShouldNotBeNull($"{c.Label}/{profile}: B5 ska bedömas i profilen (Både).");
                b5!.Verdict.ShouldNotBe(CriterionVerdict.Pass,
                    $"{c.Label}/{profile}: B5 rapporterar aldrig Pass geometri-fritt (Warn eller NotAssessed).");
            }
        }

        cleanC7Total.ShouldBeGreaterThan(0, "the clean-title stratum must be represented in the corpus.");
        // Observe-only diagnostic (not an assertion): a high clean-CV C7 Warn rate flags corpus
        // vocabulary drift (add the offending dictionary-legit tokens to the allowlist or the word bank).
        output.WriteLine($"C7 clean-CV Warn-rate (real Hunspell): {cleanC7Warns}/{cleanC7Total}.");
    }

    // ===============================================================
    // GATE (CV-pivot 2026-07-16) — the whole-CV prose rules never affirm an EMPTY corpus.
    // The empty/weak stratum has no profile and no experience text, so ReviewText.AllProse is
    // empty — pre-fix, seven rules (A7/A9/C2/C3/C4/C6/C7) each returned an affirmative Pass
    // over it ("Inga klyschor funna", "Övervägande aktivt språk", …): a claimed PRESENCE of
    // quality never observed (ADR 0109's defect class inverted). This pins the withdrawal as a
    // verdict-DISTRIBUTION property across the generated corpus, not just the unit repro.
    // ===============================================================

    private static readonly string[] ProseCriteria = ["A7", "A9", "C2", "C3", "C4", "C6", "C7"];

    [Fact]
    public async Task ProseCriteria_OnEmptyProseCases_AreNeverAffirmed_BothProfiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();

        // Filter on the ACTUAL guarded condition (no profile text, no experience text — the
        // inputs ReviewText.AllProse joins), not on the stratum label: a future weak-signal
        // variant that gains a line of profile prose must fall out of this gate instead of
        // turning it into a false FAIL (the gate asserts the guard's contract, not the
        // generator's labeling).
        var emptyProseCases = Corpus()
            .Where(c => string.IsNullOrWhiteSpace(c.Cv.Content.Profile)
                && c.Cv.Content.Experience.All(e => string.IsNullOrWhiteSpace(e.RawText)))
            .ToList();
        emptyProseCases.ShouldNotBeEmpty(
            "the corpus must carry at least one empty-prose CV (the EmptyOrWeakSignal stratum).");

        foreach (var c in emptyProseCases)
        {
            foreach (var profile in new[] { RenderProfile.Ats, RenderProfile.Visual })
            {
                var result = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), profile, ct);

                foreach (var id in ProseCriteria)
                {
                    // Fail-loud on absence: all seven are profile=Båda, so a missing verdict is
                    // itself a regression — never a silently skipped assert.
                    var verdict = result.Verdicts.SingleOrDefault(v => v.CriterionId == id);
                    verdict.ShouldNotBeNull(
                        $"{c.Label}/{profile}: {id} saknas helt i verdikts-listan.");
                    verdict!.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
                        $"{c.Label}/{profile}: {id} bedömde en tom proskorpus — vakuöst utfall.");
                }
            }
        }
    }

    private static IEnumerable<string> EvidenceStrings(CvCriterionVerdict v)
    {
        foreach (var e in v.Evidence)
        {
            switch (e)
            {
                case TextSpanEvidence ts:
                    yield return ts.Span.Quote;
                    if (ts.Note is not null) yield return ts.Note;
                    break;
                case StructuralEvidence se:
                    yield return se.Observation;
                    break;
            }
        }

        if (v.NotAssessedReason is not null) yield return v.NotAssessedReason;
    }
}
