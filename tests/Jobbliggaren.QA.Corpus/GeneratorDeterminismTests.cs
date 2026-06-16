using System.Text.Json;
using Jobbliggaren.QA.Corpus.Generation;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Fas 4 STEG C, PR 1 — the corpus generator's own determinism/reproducibility tests
/// (CTO Fork 7: the generator PR must carry its own tests so it is not unconsumed dead code;
/// coverage-gate lesson — write the consumer tests in the same STEG).
///
/// These prove the load-bearing properties the deriver/reviewer frontends rely on:
/// <list type="bullet">
/// <item><b>Reproducible</b> — same <see cref="CorpusConfig"/> → byte-identical corpus
/// (CV cases compared by content digest, since the staging aggregate's Id is a fresh
/// non-semantic Guid — the engines never key on it).</item>
/// <item><b>Index-stable under scaling</b> — a quota bump (Fork 9: 300 → 770) never shifts an
/// existing case (row <c>i</c> is a pure function of (seed, stratum, index), CTO Fork 2 = 2B).</item>
/// <item><b>Edge-over-sampling + facit wiring</b> — every stratum is represented; the
/// facit-bearing strata consume injected ground-truth (anti-stale).</item>
/// <item><b>PII discipline</b> — fake-personnummer cases trip the REAL guard and never leak the
/// raw value through a label (pre-validates ADR 0074 Invariant 1 before the reviewer frontend).</item>
/// <item><b>Crash-free generation</b> — building the full corpus (incl. adversarial/huge) never throws.</item>
/// </list>
/// </summary>
public class GeneratorDeterminismTests
{
    // Synthetic ground-truth (PR 1 is DB-free; the deriver frontend injects the REAL pairs).
    // The ssyk-4 ids need not be real here — PR 1 only proves generator behaviour, not derivation.
    private static readonly IReadOnlyList<OccupationGroundTruth> GroundTruth =
    [
        new("Mjukvaruutvecklare", "DJh5_yyF_hEM"),
        new("Advokat", "q8wL_kdi_WaW"),
        new("Förskollärare", "5ek3_Cgq_WeZ"),
        new("Snickare", "synthetic_grp_1"),
        new("Undersköterska", "synthetic_grp_2"),
    ];

    private static readonly JsonSerializerOptions DigestOptions = new() { WriteIndented = false };

    // ===============================================================
    // Reproducibility — same seed → identical corpus
    // ===============================================================

    [Fact]
    public void GenerateTitleCorpus_IsIdentical_WhenSameSeed()
    {
        var a = new CorpusGenerator().GenerateTitleCorpus(GroundTruth);
        var b = new CorpusGenerator().GenerateTitleCorpus(GroundTruth);

        // GeneratedTitleCase is a pure value record (no aggregate Id) → record equality.
        a.ShouldBe(b);
        a.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GenerateCvCorpus_HasIdenticalContent_WhenSameSeed()
    {
        var a = new CorpusGenerator().GenerateCvCorpus(GroundTruth);
        var b = new CorpusGenerator().GenerateCvCorpus(GroundTruth);

        a.Count.ShouldBe(b.Count);
        a.Count.ShouldBeGreaterThan(0);
        // Ignore the non-semantic staging-aggregate Id; compare the content the engines read.
        a.Select(CvDigest).ShouldBe(b.Select(CvDigest));
    }

    [Fact]
    public void DifferentSeed_ProducesADifferentCorpus()
    {
        var def = new CorpusGenerator().GenerateTitleCorpus(GroundTruth);
        var alt = new CorpusGenerator(CorpusConfig.Default with { Seed = 999 }).GenerateTitleCorpus(GroundTruth);

        def.Select(c => c.Title).ShouldNotBe(alt.Select(c => c.Title),
            "a different seed must drive different draws — otherwise the seed is inert.");
    }

    // ===============================================================
    // Index-stability under scaling (Fork 9 — 300 → 770 without re-generation)
    // ===============================================================

    [Fact]
    public void ScalingQuotas_DoesNotShiftExistingRows()
    {
        var scale1 = new CorpusGenerator().GenerateTitleCorpus(GroundTruth);
        var scale2 = new CorpusGenerator(CorpusConfig.Default with { Scale = 2 }).GenerateTitleCorpus(GroundTruth);

        scale2.Count.ShouldBeGreaterThan(scale1.Count, "doubling the scale must add cases.");

        // Every scale-1 case (identified by stratum+index) must appear identically in scale-2.
        var byKey2 = scale2.ToDictionary(c => (c.Stratum, c.Index));
        foreach (var c1 in scale1)
        {
            byKey2.ShouldContainKey((c1.Stratum, c1.Index));
            byKey2[(c1.Stratum, c1.Index)].ShouldBe(c1,
                $"scaling shifted {c1.Label} — index-derived determinism (2B) is broken.");
        }
    }

    // ===============================================================
    // Stratification & edge-over-sampling
    // ===============================================================

    [Fact]
    public void TitleCorpus_RepresentsEveryTitleStratum_WithConfiguredCounts()
    {
        var config = CorpusConfig.Default;
        var corpus = new CorpusGenerator(config).GenerateTitleCorpus(GroundTruth);

        foreach (var stratum in CorpusStrata.Title)
        {
            var expected = config.CountFor(stratum);
            corpus.Count(c => c.Stratum == stratum).ShouldBe(expected,
                $"stratum {stratum} should contribute exactly its configured quota.");
        }
    }

    [Fact]
    public void Corpus_OverSamplesTheEdges_RelativeToTheFacitStrata()
    {
        var corpus = new CorpusGenerator().GenerateCvCorpus(GroundTruth);

        var facit = corpus.Count(c =>
            c.Stratum is CorpusStratum.CleanExactTitle or CorpusStratum.InflectedTitle);
        var edges = corpus.Count(c =>
            c.Stratum is CorpusStratum.EmptyOrWeakSignal or CorpusStratum.LifeSituationGap
                or CorpusStratum.NonStandardOrEnglishTitle or CorpusStratum.NoiseOrOcr
                or CorpusStratum.Adversarial or CorpusStratum.FakePersonnummer);

        edges.ShouldBeGreaterThan(facit, "a stress corpus must weight the tails over the happy path.");
    }

    [Fact]
    public void FacitStrata_AreEmpty_WhenNoGroundTruthInjected()
    {
        var corpus = new CorpusGenerator().GenerateTitleCorpus([]);

        corpus.ShouldNotContain(c => c.Stratum == CorpusStratum.CleanExactTitle);
        corpus.ShouldNotContain(c => c.Stratum == CorpusStratum.InflectedTitle);
        corpus.ShouldNotContain(c => c.Stratum == CorpusStratum.MultiTrack);
        // The self-contained strata still generate.
        corpus.ShouldContain(c => c.Stratum == CorpusStratum.LifeSituationGap);
    }

    [Fact]
    public void CleanExactTitleCases_CarryTheFacitGroupFromGroundTruth()
    {
        var corpus = new CorpusGenerator().GenerateTitleCorpus(GroundTruth);
        var gtNames = GroundTruth.Select(g => g.OccupationName).ToHashSet();
        var gtGroups = GroundTruth.Select(g => g.ExpectedSsyk4ConceptId).ToHashSet();

        var clean = corpus.Where(c => c.Stratum == CorpusStratum.CleanExactTitle).ToList();
        clean.ShouldNotBeEmpty();
        foreach (var c in clean)
        {
            c.Expectation.ShouldBe(DerivationExpectation.ResolvesToFacitGroup);
            c.Title.ShouldBeOneOf([.. gtNames]);
            c.ExpectedSsyk4ConceptId.ShouldNotBeNull();
            gtGroups.ShouldContain(c.ExpectedSsyk4ConceptId);
        }

        corpus.Where(c => c.Stratum == CorpusStratum.LifeSituationGap)
            .ShouldAllBe(c => c.Expectation == DerivationExpectation.NeverResolvesToSsyk);
    }

    // ===============================================================
    // PII discipline (pre-validates ADR 0074 Invariant 1 before the reviewer frontend)
    // ===============================================================

    [Fact]
    public void FakePersonnummerCvs_TripTheRealGuard()
    {
        var corpus = new CorpusGenerator().GenerateCvCorpus(GroundTruth);
        var pnrCases = corpus.Where(c => c.Stratum == CorpusStratum.FakePersonnummer).ToList();

        pnrCases.ShouldNotBeEmpty();
        foreach (var c in pnrCases)
        {
            c.ExpectsPersonnummerFlagged.ShouldBeTrue();
            c.Cv.Personnummer.Found.ShouldBeTrue($"the real scanner must detect the fake pnr in {c.Label}.");
            c.Cv.Personnummer.Count.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void OnlyFakePersonnummerStratum_FlagsAPersonnummer()
    {
        // Honest outcome: no other stratum's synthetic text accidentally carries a Luhn-valid pnr.
        var corpus = new CorpusGenerator().GenerateCvCorpus(GroundTruth);

        corpus.Where(c => c.Cv.Personnummer.Found)
            .ShouldAllBe(c => c.Stratum == CorpusStratum.FakePersonnummer);
    }

    [Fact]
    public void CaseLabels_NeverLeakARawPersonnummer()
    {
        var corpus = new CorpusGenerator().GenerateCvCorpus(GroundTruth);

        foreach (var c in corpus)
        {
            // The PII-safe label is stratum+index only — never digit runs that could be a pnr.
            c.Label.ShouldNotContain("-98");
            foreach (var fake in SwedishCorpusLexicon.FakePersonnummer)
                c.Label.ShouldNotContain(fake);
        }
    }

    // ===============================================================
    // Crash-free generation
    // ===============================================================

    [Fact]
    public void GeneratingTheFullCorpus_NeverThrows()
    {
        Should.NotThrow(() =>
        {
            var gen = new CorpusGenerator();
            _ = gen.GenerateTitleCorpus(GroundTruth);
            _ = gen.GenerateCvCorpus(GroundTruth);
        });
    }

    [Fact]
    public void StableSeed_IsAPureFunctionOfItsCoordinates()
    {
        DeterministicRng.StableSeed(7, CorpusStratum.Adversarial, 3)
            .ShouldBe(DeterministicRng.StableSeed(7, CorpusStratum.Adversarial, 3));
        DeterministicRng.StableSeed(7, CorpusStratum.Adversarial, 3)
            .ShouldNotBe(DeterministicRng.StableSeed(7, CorpusStratum.Adversarial, 4));
        DeterministicRng.StableSeed(7, CorpusStratum.Adversarial, 3)
            .ShouldBeGreaterThanOrEqualTo(0, "the seed must be a non-negative int for Random.");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string CvDigest(GeneratedCvCase c) =>
        string.Join('|',
            c.Stratum,
            c.Index,
            c.ExpectsPersonnummerFlagged,
            c.Cv.DetectedLanguage,
            c.Cv.RawText,
            $"{c.Cv.Personnummer.Found}:{c.Cv.Personnummer.Count}",
            JsonSerializer.Serialize(c.Cv.Content, DigestOptions));
}
