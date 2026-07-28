using Jobbliggaren.QA.Corpus.Harness;
using Jobbliggaren.QA.Corpus.Layout;
using Jobbliggaren.QA.Corpus.Reporting;
using Shouldly;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// PR K (#1060) — the layout fitness corpus. It authors 21 CV documents as real PDF and DOCX
/// BYTES and drives each of them through the product's own chain: <c>CvFileSignature</c> →
/// <c>ImportResumeCommandHandler</c> (extract, personnummer scan, segment, <c>ParsedResume.Create</c>)
/// → <c>AutoPromoteParsedResumeCommandHandler</c> (five gates, DQ6 among them, the internal content
/// mapper, <c>Resume.CreateFromParsed</c>). No database server, no container, no network.
///
/// <para>Both numerals above were wrong and were corrected 2026-07-28. What follows is the
/// MEASUREMENT rather than a characterisation of it, because two reviewers and I each produced a
/// different characterisation from the same history and all three were wrong somewhere:</para>
/// <code>
/// 980a00d4  16 cases  "sixteen"   &lt;- true when written
/// ccda80d0  17 cases  "sixteen"   &lt;- went false inside PR K's OWN review round
/// d9e0af7f  17 cases  "sixteen"   &lt;- shipped false
/// 7a5496fe  21 cases  "sixteen"   &lt;- PR E drifted it further
/// </code>
/// <para>"six gates" is the simpler kind: true until PR B retired the preamble gate. A numeral
/// beside a catalog anyone can count needs no such archaeology, which is why the count is now a
/// digit — and why this paragraph states four measurements instead of one adjective.</para>
///
/// <para><b>The material difference from the existing corpus.</b> <c>CorpusGenerator</c> starts
/// DOWNSTREAM of the segmenter — it calls <c>ParsedResume.Create</c> with pre-built content and
/// never invokes the segmenter, a fact ADR 0109 §4 records. It therefore could never see the
/// defect that motivated this work. This suite starts UPSTREAM of the extractor, from bytes.</para>
///
/// <para><b>Observe-only</b> (CLAUDE.md §2.5, CTO R3). Not a CI gate. The assert rule is narrow
/// and deliberate:</para>
///
/// <para><i>A hard assert may only take as its subject (a) the bytes this corpus authored, read
/// through a NON-PRODUCTION reader, (b) the corpus's own declarations, or (c) its own emitter.
/// Everything the production chain produces — blank-line counts, heading detection, entry counts,
/// confidence, gate verdicts, promote booleans, content-loss deltas — is RECORDED, never
/// asserted.</i></para>
///
/// <para>That rule is what stops this suite from blocking its own remedy: asserting
/// <c>BlankLineCount == 0</c> would turn every PDF case red the day a boundary fix lands, on a
/// suite whose whole purpose is to measure that fix's effect. (PR E built one and withdrew it;
/// the rule matters exactly as much for the attempt that follows.)</para>
///
/// <para><b>The escape hatch, stated once so it can be cited.</b> An assert whose subject falls
/// outside (a)/(b)/(c) is permitted when it is ARGUED where it lives. FOUR do so in this file —
/// crash-safety, kind resolution, marker visibility, and (since 2026-07-28) ladder
/// well-formedness — and one more in <c>LayoutCorpusEmitterTests</c>, whose subject is
/// <c>AutoPromoteBlockReason</c>'s declared member set. The rule's exclusion is
/// <i>everything the production chain PRODUCES</i>; a declared type surface is not that, and no
/// parsing change can move it. No parsing OUTCOME is asserted anywhere.</para>
///
/// <para>The artifact is written BEFORE any assert runs, so even a breach leaves a complete,
/// readable report on disk.</para>
/// </summary>
public sealed class LayoutCorpusReportTests
{
    /// <summary>The commit this baseline was produced at. It has two homes — here and the
    /// committed baseline's own header — and nothing checks them against each other, so bump it
    /// DELIBERATELY when regenerating, never as a side effect. A stale value would make the
    /// provenance string F3 exists for untrustworthy.
    ///
    /// <para><b>It was stale, and the warning above is why that is worth writing down rather than
    /// quietly overwriting.</b> The value read <c>7a5496fe</c> (PR E's merge) while
    /// <c>git log -- baseline/layout-corpus-report.baseline.md</c> shows the file was last
    /// regenerated in <c>a72c77e7</c> (PR B). So the published numbers were post-B behaviour
    /// carrying a pre-B provenance string — a claim true of the run that first produced the file
    /// and false of the one that last wrote it. Corrected 2026-07-28 to this branch's base.</para>
    /// </summary>
    private const string BaseCommit = "3aa46b47";

    [Fact]
    public async Task LayoutCorpus_FromBytes_EmitsReport()
    {
        var ct = TestContext.Current.CancellationToken;

        var observations = new List<LayoutCaseObservation>();
        foreach (var layoutCase in LayoutCaseCatalog.All)
            observations.Add(await LayoutChainRunner.RunAsync(layoutCase, ct));

        var markdown = LayoutCorpusReport.Build(new LayoutCorpusReportData(
            BaseCommit, observations, CvChainProbe.SubstitutedPorts,
            LayoutCaseCatalog.ValidateModelSymmetry()));

        await WriteArtifactAsync(markdown, ct);

        // ── Instrument asserts only, from here down. Failure means the FIXTURE or the EMITTER is
        // broken; none of these can be reddened by a change to the product's parsing behaviour.

        // FOUR of the asserts below ARE reachable by production behaviour, and each has its own
        // argument. Stated plainly rather than claimed away, because an earlier revision of this
        // comment said "none of these can be reddened by a change to the product" and that was
        // simply false — and a later one said "three" after a fourth had been added.
        //
        //   (a) crash-safety — the probe catches everything the real extractor, segmenter and both
        //       handlers throw, so a throw anywhere in the chain reddens this suite. That is
        //       correct: a corpus that swallows a product crash is not measuring anything.
        //   (b) kind resolution — the subject is production Domain (CvFileSignature). Correct for
        //       the same reason: a case never fed to the handler as the kind it claims measures
        //       nothing.
        //   (c) marker visibility — argued at its own assert below.
        //   (d) ladder well-formedness — argued at its own assert below. NEW 2026-07-28: it used
        //       to be unreachable by construction and is not any more.
        //
        // Every OTHER production output (blank lines, entry counts, confidence, WHICH gate blocked,
        // promote booleans, the content-loss delta) is recorded, never asserted. Note the narrowing
        // in that list: the ladder's SHAPE is now asserted while the VERDICT it renders still is
        // not — "gate verdicts" unqualified stopped being true when (d) landed.
        var crashed = observations.Where(o => o.CrashedWithExceptionType is not null).ToList();
        crashed.ShouldBeEmpty(
            "INSTRUMENT: a case threw. Types: "
            + string.Join(", ", crashed.Select(o => $"{o.Case.Id}={o.CrashedWithExceptionType}")));

        var badBytes = observations.Where(o => o.ByteProofFailure is not null).ToList();
        badBytes.ShouldBeEmpty(
            "INSTRUMENT: a case's authored bytes do not have the form it claims. "
            + string.Join(" | ", badBytes.Select(o => o.ByteProofFailure)));

        var badFixture = observations.Where(o => o.FixtureProblems.Count > 0).ToList();
        badFixture.ShouldBeEmpty(
            "INSTRUMENT: a model cannot carry the marker oracle. "
            + string.Join(" | ", badFixture.SelectMany(o => o.FixtureProblems)));

        LayoutCaseCatalog.ValidateModelSymmetry().ShouldBeEmpty(
            "INSTRUMENT: the English model is no longer structurally identical to the Swedish one, "
            + "so pin P5's non-difference claim is noise rather than a measurement.");

        // Production-touching assert (d), argued here. TWO causes, and the FIRST is the one a
        // reader will actually hit: the ladder has no arm for the reason the handler returned, so
        // it cannot place the block. That is reachable by a product change — a new
        // AutoPromoteBlockReason reddens this — and red is the right answer, because the
        // alternative is what shipped before: the artifact narrating an unmapped token as a
        // handler fault while §0 reported the instrument healthy. It does not block its own
        // remedy: the remedy is one arm in GateLadder, in this same suite.
        observations.Where(o => !GateLadder.IsWellFormed(o.Gates)).ShouldBeEmpty(
            "INSTRUMENT: a gate ladder is not well-formed. Either (1) it has no arm for the reason "
            + "the handler returned — most likely a new AutoPromoteBlockReason — or (2) it reports "
            + "a rung as passed after one that was never evaluated, which is impossible.");

        observations.Where(o => !o.KindResolved).ShouldBeEmpty(
            "INSTRUMENT: a case's bytes failed CvFileSignature.TryResolve, so it was never fed to "
            + "the handler as the kind it claims to be.");

        // The ONE deliberate production-touching assert, argued rather than smuggled in.
        //
        // A corpus that cannot see its own content must REFUSE TO REPORT rather than report loss:
        // without this, a renderer that silently dropped a job would print a zero delta as "no
        // loss", and a broken extractor would print a full delta as a finding. It stays an assert
        // because no plausible PR B/C/E change removes an authored employer name from the
        // EXTRACTED TEXT — a change that does is a genuine regression and red is the right answer.
        var invisible = observations
            .Where(o => o.Markers.Any(m => !m.InExtractedBytes))
            .Select(o => $"{o.Case.Id} [{o.ExtractionStatus}]: "
                + string.Join(",", o.Markers.Where(m => !m.InExtractedBytes).Select(m => m.Marker)))
            .ToList();

        invisible.ShouldBeEmpty(
            "EXTRACTION: the corpus cannot see content it authored, so no row below it means "
            + "anything. Verify the extractor before reading any result. " + string.Join(" | ", invisible));
    }

    private static async Task WriteArtifactAsync(string markdown, CancellationToken ct)
    {
        // Deterministic path, no timestamp in the filename (parity CorpusFindingsReportTests):
        // walk up from bin/Debug/net10.0 to the project root, then artifacts/. Gitignored twice
        // (.gitignore:32 and :43) — the EMITTER is the deliverable.
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "layout-corpus-report.md"), markdown, ct);
    }
}
