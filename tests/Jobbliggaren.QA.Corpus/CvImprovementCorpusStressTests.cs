using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Improvement;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.QA.Corpus.Generation;
using Jobbliggaren.QA.Corpus.Harness;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Fas 4 STEG B-2 — improvement-evidence-redaction hardening, corpus frontend (CTO-bound,
/// <c>docs/reviews/2026-06-17-f4-improvement-evidence-redaction-*.md</c>; ADR 0074 Invariant 1;
/// ADR 0071 NO AI/LLM). Runs the seeded CV corpus through the REAL <see cref="CvImprovementEngine"/>
/// (F4-10) — NOT mocked — wired to the REAL knowledge bank (cliché/verb/rubric loaders) and the
/// REAL Swedish Snowball analyzer (parity <c>CvReviewCorpusStressTests.NewEngine()</c>). Pure
/// in-memory: no DB, no Testcontainer (the engine consumes an in-memory <c>ParsedResume</c>;
/// field-encryption is the handler's concern, not the engine's).
///
/// <para>GATING (CTO D6 — the only assertions; per-transform hit-rate is observe-only):</para>
/// <list type="number">
/// <item><b>Crash-safety = 100%</b> across every stratum, BOTH render profiles (CrashSweep).</item>
/// <item><b>PII-safety (the gating whole-corpus invariant, ADR 0074 Inv. 1):</b> NO
/// <see cref="ProposedChange"/> field echoes ANY <c>SwedishCorpusLexicon.FakePersonnummer</c> over
/// the whole corpus × both profiles — the engine redacts pnr via PersonnummerRedactor before result
/// assembly (the analogue of the review engine's
/// <c>NoReviewEvidence_EchoesARawPersonnummer_AcrossTheWholeCorpus</c>).</item>
/// <item><b>FakePersonnummer stratum:</b> the B4 PersonnummerStrip transform fires, and its
/// StructuralEvidence / Operation.Target are count-only (never the raw value).</item>
/// <item><b>Wiring anchor:</b> the corpus produces changes (real rules ran, not a no-op).</item>
/// </list>
///
/// <para>RED PHASE: the engine does NOT yet redact — DateNormalization quotes a period bearing a
/// pnr. Gate 2 FAILS until CC adds the pass; CC writes production to green afterward.</para>
/// </summary>
public class CvImprovementCorpusStressTests
{
    // The improver does not derive SSYK, so CV titles need not be real taxonomy labels — a small
    // synthetic ground-truth keeps this frontend pure in-memory (parity the review corpus test).
    private static readonly IReadOnlyList<OccupationGroundTruth> SyntheticGroundTruth =
    [
        new("Mjukvaruutvecklare", "synthetic"),
        new("Sjuksköterska", "synthetic"),
        new("Snickare", "synthetic"),
        new("Ekonomiassistent", "synthetic"),
        new("Lärare", "synthetic"),
    ];

    // Fas 4b 8b.4b — the REAL committed lexicon + conventions asset, cross-asset-pinned in the
    // provider's ctor (so the corpus stresses the same pair the host boots with).
    private static CvImprovementEngine NewEngine()
    {
        var lexiconData = CvParsingLexiconLoader.Load();
        var conventions = new CvConventionsProvider(new CvParsingLexiconProvider(lexiconData));
        return new CvImprovementEngine(
            new ClicheLexicon(), new VerbMapper(), new RubricProvider(),
            new LocalTextAnalyzer(new SnowballStemmer()),
            conventions, lexiconData);
    }

    private static IReadOnlyList<GeneratedCvCase> Corpus() =>
        new CorpusGenerator().GenerateCvCorpus(SyntheticGroundTruth);

    public static TheoryData<RenderProfile> BothProfiles() =>
        new() { RenderProfile.Ats, RenderProfile.Visual };

    // Every user-text string a ProposedChange exposes (the exhaustive field sweep, CTO D6).
    private static IEnumerable<string> ProposedChangeStrings(ProposedChange change)
    {
        switch (change.Evidence)
        {
            case TextSpanEvidence ts:
                yield return ts.Span.Quote;
                if (ts.Note is not null) yield return ts.Note;
                break;
            case StructuralEvidence se:
                yield return se.Observation;
                break;
        }

        if (change.Replacement is not null)
        {
            yield return change.Replacement.Before;
            yield return change.Replacement.After;
        }

        if (change.Operation is not null) yield return change.Operation.Target;
        yield return change.Rationale;
        yield return change.Provenance switch
        {
            KnowledgeBankProvenance kb => kb.Key,
            _ => string.Empty,
        };
    }

    // ===============================================================
    // GATE 1 — Crash-safety MUST be 100% across every stratum, both profiles (CTO Fork 5C parity)
    // ===============================================================

    [Fact]
    public async Task CvImprovementCorpus_IsCrashSafe_AcrossEveryStratum_BothProfiles()
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
                _ = await engine.SuggestAsync(c.Cv, review: null, RenderProfile.Ats, t);
                _ = await engine.SuggestAsync(c.Cv, review: null, RenderProfile.Visual, t);
            },
            ct);

        var crashes = outcomes.Crashes();
        crashes.ShouldBeEmpty(
            "KRASCH-SÄKERHET BRUTEN — förbättringsmotorn kastade på: " +
            string.Join(", ", crashes.Select(x => $"{x.Label} ({x.ExceptionType})")));

        foreach (var stratumGroup in outcomes.GroupBy(o => o.Stratum))
            stratumGroup.Count(o => o.Threw).ShouldBe(0,
                $"strata {stratumGroup.Key} ska vara 100% krasch-fri.");
    }

    // ===============================================================
    // GATE 2 — PII-safety (ADR 0074 Invariant 1), THE GATING whole-corpus invariant: NO
    // ProposedChange field echoes a raw pnr across the corpus + both profiles. The engine redacts
    // via PersonnummerRedactor before result assembly. (The STEG B period-vector finding turned
    // into a passing regression gate — analogue of the review engine's
    // NoReviewEvidence_EchoesARawPersonnummer_AcrossTheWholeCorpus.)
    //
    // The corpus is augmented with a LEAK VARIANT per FakePersonnummer case (PnrPeriodLeakCorpus):
    // the same aggregate, rebuilt with a NON-CANONICAL period carrying the pnr, so the
    // DateNormalization transform actually QUOTES it. Without this, the as-generated corpus uses
    // canonical periods ("2020–2024") and no improvement transform ever quotes the pnr, so the gate
    // would pass VACUOUSLY (a false green). This is the documented STEG-B difference between the
    // review engine (A8 quotes the whole profile → leaks as-is) and the improvement engine (narrow
    // phrase/prefix/period quotes → only the period vector leaks). See the report.
    // ===============================================================

    [Fact]
    public async Task NoProposedChange_EchoesARawPersonnummer_AcrossTheWholeCorpus()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();

        foreach (var c in PnrPeriodLeakCorpus())
        {
            foreach (var profile in new[] { RenderProfile.Ats, RenderProfile.Visual })
            {
                var result = await engine.SuggestAsync(c.Cv, review: null, profile, ct);

                foreach (var s in result.Changes.SelectMany(ProposedChangeStrings))
                    foreach (var fake in SwedishCorpusLexicon.FakePersonnummer)
                        s.Contains(fake, StringComparison.Ordinal).ShouldBeFalse(
                            $"{c.Label}/{profile}: a ProposedChange field echoed a raw personnummer — " +
                            "the engine's evidence-redaction regressed (ADR 0074 Inv. 1).");
            }
        }
    }

    // The whole CV corpus PLUS, for every FakePersonnummer case, a leak variant that drives the pnr
    // into a NON-CANONICAL period so the DateNormalization transform quotes it. PII-safe by
    // construction: the raw value never reaches a Label (the variant reuses the original Label).
    private static IReadOnlyList<GeneratedCvCase> PnrPeriodLeakCorpus()
    {
        var corpus = Corpus();
        var variants = corpus
            .Where(c => c.Stratum == CorpusStratum.FakePersonnummer)
            .Select(WithPnrBearingPeriod)
            .ToList();
        return [.. corpus, .. variants];
    }

    // Rebuilds a corpus CV (via the same ParsedResume.Create path the generator uses) with its
    // first experience's PERIOD replaced by a non-canonical, pnr-bearing string. PeriodParser
    // rejects "jan 2022 - <pnr>" (month NAME), so DateNormalization fires and quotes the period.
    private static GeneratedCvCase WithPnrBearingPeriod(GeneratedCvCase original)
    {
        var pnr = SwedishCorpusLexicon.FakePersonnummer[0]; // Luhn-valid 811218-9876
        var pnrPeriod = $"jan 2022 - {pnr}";

        var content = original.Cv.Content;
        var firstExperience = content.Experience[0];
        var leakExperience = firstExperience with
        {
            Period = pnrPeriod,
            RawText = $"{firstExperience.RawText} Period: {pnrPeriod}.",
        };
        var leakContent = content with
        {
            Experience = [leakExperience, .. content.Experience.Skip(1)],
        };

        var rawText = $"{original.Cv.RawText}\n{pnrPeriod}";
        var personnummer = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize($"{pnr} {pnr}")));

        var created = ParsedResume.Create(
            original.Cv.JobSeekerId,
            "CV_FakePersonnummer_PeriodLeak.pdf",
            "application/pdf",
            original.Cv.DetectedLanguage,
            leakContent,
            rawText,
            original.Cv.Confidence,
            personnummer,
            [],
            FixedClock.Default);

        created.IsSuccess.ShouldBeTrue(
            "the pnr-period leak variant must be a structurally valid CV (generator-parity build).");

        return original with { Cv = created.Value };
    }

    // ===============================================================
    // GATE 3 — FakePersonnummer stratum: the B4 PersonnummerStrip transform fires, and its evidence
    // / operation target are count-only (never the raw value).
    // ===============================================================

    [Fact]
    public async Task FakePersonnummerCvs_ProduceAPiiSafePersonnummerStrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();
        var pnrCases = Corpus().Where(c => c.Stratum == CorpusStratum.FakePersonnummer).ToList();
        pnrCases.ShouldNotBeEmpty();

        foreach (var c in pnrCases)
        {
            var result = await engine.SuggestAsync(c.Cv, review: null, RenderProfile.Ats, ct);

            var strip = result.Changes
                .SingleOrDefault(ch => ch.Kind == ProposedChangeKind.PersonnummerStrip);
            strip.ShouldNotBeNull(
                $"{c.Label}: a flagged personnummer must produce a PersonnummerStrip change (B4).");

            // The strip change is a pure removal: structural evidence + count-only operation target.
            strip!.Evidence.ShouldBeOfType<StructuralEvidence>(
                $"{c.Label}: the strip change cites structurally (count only), never a text span.");
            strip.Replacement.ShouldBeNull($"{c.Label}: a pure removal carries no before/after text.");

            foreach (var s in ProposedChangeStrings(strip))
                foreach (var fake in SwedishCorpusLexicon.FakePersonnummer)
                    s.Contains(fake, StringComparison.Ordinal).ShouldBeFalse(
                        $"{c.Label}: the PersonnummerStrip change echoed a raw personnummer (Inv. 1).");
        }
    }

    // ===============================================================
    // GATE 4 — Wiring anchor: the corpus produces changes (real rules ran against the real KB, not
    // a stub returning empty).
    //
    // NOTE (deviation from the literal prompt, reported to CC): the prompt asked for "CleanExactTitle
    // produces ≥1 change", but the generator's clean CV (StrongCv) is deliberately strong — clean
    // profile, strong opening verb, canonical period — so it legitimately yields ZERO changes (the
    // improvement engine proposes only when there is something to fix; unlike the review engine,
    // which always emits verdicts). Asserting ≥1 change on a clean CV would contradict the engine's
    // design. The wiring intent ("real rules ran, not a no-op") is bound instead to (a) the whole
    // corpus producing changes and (b) the FakePersonnummer stratum always producing a strip.
    // ===============================================================

    [Fact]
    public async Task Improver_IsWiredToRealKnowledgeBank_AndProposesChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = NewEngine();

        var total = 0;
        foreach (var c in Corpus())
        {
            var result = await engine.SuggestAsync(c.Cv, review: null, RenderProfile.Ats, ct);

            // Self-describing envelope is honestly stamped (proves the real loaders ran).
            result.Profile.ShouldBe(RenderProfile.Ats, $"{c.Label}: the result echoes the requested profile.");
            result.ClicheListVersion.ShouldNotBeNullOrWhiteSpace($"{c.Label}: a real cliché version is stamped.");
            result.VerbMappingVersion.ShouldNotBeNullOrWhiteSpace($"{c.Label}: a real verb-mapping version is stamped.");

            total += result.Changes.Count;
        }

        total.ShouldBeGreaterThan(0,
            "the corpus must drive at least one proposed change (real transforms ran, not a no-op).");
    }
}
